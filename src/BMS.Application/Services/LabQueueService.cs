using System.Security.Cryptography;
using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Service orchestrating laboratory sample queuing, sample collection time recording (Record Sample Time),
/// ISO 15189 lifecycle status transitions (Update Sample Status), and STAT prioritization (UC17).
/// </summary>
public class LabQueueService : ILabQueueService
{
    /// <summary>
    /// Permitted ISO 15189 specimen lifecycle state machine transitions.
    /// Note: Processing and Testing are clinical synonyms in BMS_Database.
    /// </summary>
    private static readonly IReadOnlyDictionary<SampleStatus, SampleStatus[]> AllowedTransitions =
        new Dictionary<SampleStatus, SampleStatus[]>
        {
            [SampleStatus.PendingCollection] =
            [
                SampleStatus.Collected,
                SampleStatus.Rejected
            ],
            [SampleStatus.Collected] =
            [
                SampleStatus.Collected, // idempotent collection time update
                SampleStatus.InTransit,
                SampleStatus.Received,
                SampleStatus.Processing,
                SampleStatus.Testing,
                SampleStatus.Rejected
            ],
            [SampleStatus.InTransit] =
            [
                SampleStatus.Received,
                SampleStatus.Processing,
                SampleStatus.Testing,
                SampleStatus.Rejected
            ],
            [SampleStatus.Received] =
            [
                SampleStatus.Processing,
                SampleStatus.Testing,
                SampleStatus.Completed,
                SampleStatus.Rejected
            ],
            [SampleStatus.Processing] =
            [
                SampleStatus.Testing,
                SampleStatus.Completed,
                SampleStatus.RerunRequested,
                SampleStatus.Rejected
            ],
            [SampleStatus.Testing] =
            [
                SampleStatus.Processing,
                SampleStatus.Completed,
                SampleStatus.RerunRequested,
                SampleStatus.Rejected
            ],
            [SampleStatus.RerunRequested] =
            [
                SampleStatus.Processing,
                SampleStatus.Testing,
                SampleStatus.Completed,
                SampleStatus.Rejected
            ],
            [SampleStatus.Completed] =
            [
                SampleStatus.RerunRequested
            ],
            [SampleStatus.Rejected] = []
        };

    private readonly BmsDbContext _context;
    private readonly IMongoDatabase? _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<LabQueueService>? _logger;

    public LabQueueService(
        BmsDbContext context,
        IMongoDatabase? db = null,
        TimeProvider? clock = null,
        ILogger<LabQueueService>? logger = null)
    {
        _context = context;
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
    }

    private IMongoCollection<Sample>? SamplesCol => _db?.GetCollection<Sample>("blood_samples");
    private IMongoCollection<Patient>? PatientsCol => _db?.GetCollection<Patient>("patients");
    private IMongoCollection<BsonDocument>? AppointmentsCol => _db?.GetCollection<BsonDocument>("appointments");
    private IMongoCollection<User>? AccountsCol => _db?.GetCollection<User>("accounts");
    private IMongoCollection<BsonDocument>? RfidLogsCol => _db?.GetCollection<BsonDocument>("rfid_tracking_logs");
    private IMongoCollection<TestResult>? TestResultsCol => _db?.GetCollection<TestResult>("test_results");

    /// <summary>
    /// Retrieves filtered, prioritized specimen list for clinical laboratory technicians.
    /// Merges MongoDB <c>blood_samples</c> with <c>BmsDbContext.Samples</c>.
    /// </summary>
    public async Task<(List<LabQueueItemDto> Items, int TotalCount)> GetQueueAsync(QueueFilterDto filter)
    {
        var allSamples = await LoadAllSamplesEnrichedAsync();
        var query = allSamples.AsQueryable();

        // 1. Status filter
        if (!string.IsNullOrWhiteSpace(filter.Status) && !filter.Status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<SampleStatus>(filter.Status.Trim(), true, out var statusEnum))
            {
                if (statusEnum is SampleStatus.Processing or SampleStatus.Testing)
                {
                    query = query.Where(s => s.Status == SampleStatus.Processing || s.Status == SampleStatus.Testing);
                }
                else
                {
                    query = query.Where(s => s.Status == statusEnum);
                }
            }
        }

        // 2. Priority filter
        if (!string.IsNullOrWhiteSpace(filter.Priority) && !filter.Priority.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<SamplePriority>(filter.Priority.Trim(), true, out var prioEnum))
            {
                query = query.Where(s => s.Priority == prioEnum);
            }
        }

        // 3. Patient / Appointment filter
        if (!string.IsNullOrWhiteSpace(filter.PatientId))
        {
            var pid = filter.PatientId.Trim();
            query = query.Where(s => s.PatientId.Equals(pid, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.AppointmentId))
        {
            var aid = filter.AppointmentId.Trim();
            query = query.Where(s => s.TestOrderId.Equals(aid, StringComparison.OrdinalIgnoreCase));
        }

        // 4. Search query (Barcode, Sample ID, Patient ID, Patient Name, or Package Name)
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            query = query.Where(x =>
                x.Id.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                x.Barcode.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                x.PatientId.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                x.PatientName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                x.TestPackageName.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        // 5. Lab Room filter
        if (!string.IsNullOrWhiteSpace(filter.LabRoom) && !filter.LabRoom.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.LabRoom != null && x.LabRoom.Contains(filter.LabRoom.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var total = query.Count();
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        // Sort by priority first (Stat, Urgent, Normal), then CollectedAt descending
        var paged = query
            .OrderByDescending(s => s.Priority)
            .ThenByDescending(s => s.CollectedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToDto)
            .ToList();

        return (paged, total);
    }

    public async Task<LabQueueItemDto?> GetSampleByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var sample = await FindSampleAsync(id.Trim());
        if (sample == null)
            return null;

        await EnrichSampleMetadataAsync([sample]);
        return MapToDto(sample);
    }

    /// <inheritdoc />
    /// <summary>
    /// UC40 - Record Sample Time (Issue I2-061 | BR-122 / BR-PHL-01 / BR-PHL-02).
    /// Records blood specimen collection timestamp, associates tube barcode/RFID,
    /// verifies sample preservation limits, and transitions lifecycle status to Collected.
    /// </summary>
    public async Task<LabQueueItemDto> RecordSampleCollectionTimeAsync(
        string? idOrBarcode,
        RecordSampleCollectionRequest request,
        string? operatorUserId = null,
        CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        // 1. Validate collection timestamp
        var collectedAt = request.CollectedAt.HasValue
            ? (request.CollectedAt.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(request.CollectedAt.Value, DateTimeKind.Utc)
                : request.CollectedAt.Value.ToUniversalTime())
            : now;

        if (collectedAt > now.AddMinutes(2))
        {
            throw new InvalidSampleTimestampException(
                $"Thời điểm lấy mẫu ({collectedAt:yyyy-MM-dd HH:mm:ss} UTC) không được vượt quá thời gian hiện tại của hệ thống ({now:yyyy-MM-dd HH:mm:ss} UTC).");
        }

        if (collectedAt < now.AddDays(-30))
        {
            throw new InvalidSampleTimestampException(
                "Thời điểm lấy mẫu không hợp lệ (không được lùi quá 30 ngày trước).");
        }

        var targetKey = !string.IsNullOrWhiteSpace(idOrBarcode)
            ? idOrBarcode.Trim()
            : !string.IsNullOrWhiteSpace(request.SampleId)
                ? request.SampleId.Trim()
                : !string.IsNullOrWhiteSpace(request.Barcode)
                    ? request.Barcode.Trim()
                    : null;

        Sample? sample = null;
        bool isNewSample = false;

        if (!string.IsNullOrWhiteSpace(targetKey))
        {
            sample = await FindSampleAsync(targetKey, ct);
        }

        // If targetKey was explicitly specified in route (idOrBarcode) and sample not found -> throw 404
        if (sample == null && !string.IsNullOrWhiteSpace(idOrBarcode))
        {
            throw new SampleNotFoundException(idOrBarcode);
        }

        // Validate patient & appointment association
        var patientId = !string.IsNullOrWhiteSpace(request.PatientId)
            ? request.PatientId.Trim()
            : sample?.PatientId;

        var appointmentId = !string.IsNullOrWhiteSpace(request.AppointmentId)
            ? request.AppointmentId.Trim()
            : sample?.TestOrderId;

        if (sample == null)
        {
            if (string.IsNullOrWhiteSpace(patientId) && string.IsNullOrWhiteSpace(appointmentId))
            {
                throw new DomainValidationException(
                    "SAMPLE_ASSOCIATION_REQUIRED",
                    "Vui lòng cung cấp mã mẫu (SampleId/Barcode) hoặc thông tin liên kết Bệnh nhân (PatientId) / Lịch hẹn (AppointmentId).");
            }
            isNewSample = true;
        }
        else
        {
            // Verify patient / appointment consistency if caller passed both
            if (!string.IsNullOrWhiteSpace(request.PatientId) &&
                !string.IsNullOrWhiteSpace(sample.PatientId) &&
                !sample.PatientId.Equals(request.PatientId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainValidationException(
                    "SAMPLE_PATIENT_MISMATCH",
                    $"Mẫu '{sample.Id}' đang thuộc về bệnh nhân '{sample.PatientId}', không khớp với mã bệnh nhân '{request.PatientId}'.");
            }

            if (!string.IsNullOrWhiteSpace(request.AppointmentId) &&
                !string.IsNullOrWhiteSpace(sample.TestOrderId) &&
                !sample.TestOrderId.Equals(request.AppointmentId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainValidationException(
                    "SAMPLE_APPOINTMENT_MISMATCH",
                    $"Mẫu '{sample.Id}' đang thuộc về lịch hẹn '{sample.TestOrderId}', không khớp với lịch hẹn '{request.AppointmentId}'.");
            }
        }

        // Validate against appointment in MongoDB if present
        BsonDocument? appointmentDoc = null;
        if (!string.IsNullOrWhiteSpace(appointmentId) && AppointmentsCol != null)
        {
            appointmentDoc = await AppointmentsCol
                .Find(Builders<BsonDocument>.Filter.Eq("_id", appointmentId))
                .FirstOrDefaultAsync(ct);

            if (appointmentDoc != null)
            {
                var appPatientId = appointmentDoc.TryGetValue("patient_id", out var pidVal) && pidVal.IsString
                    ? pidVal.AsString
                    : null;

                if (!string.IsNullOrWhiteSpace(patientId) &&
                    !string.IsNullOrWhiteSpace(appPatientId) &&
                    !string.Equals(patientId, appPatientId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DomainValidationException(
                        "PATIENT_APPOINTMENT_MISMATCH",
                        $"Lịch hẹn '{appointmentId}' thuộc về bệnh nhân '{appPatientId}', không khớp với bệnh nhân '{patientId}'.");
                }

                patientId ??= appPatientId;

                if (appointmentDoc.TryGetValue("check_in_time", out var checkInVal) && checkInVal.IsValidDateTime)
                {
                    var checkInUtc = checkInVal.ToUniversalTime();
                    if (collectedAt < checkInUtc.AddHours(-1))
                    {
                        throw new InvalidSampleTimestampException(
                            $"Thời điểm lấy mẫu ({collectedAt:yyyy-MM-dd HH:mm:ss} UTC) không được sớm hơn thời điểm check-in của lịch hẹn ({checkInUtc:yyyy-MM-dd HH:mm:ss} UTC).");
                    }
                }
            }
        }

        // Validate patient exists if PatientsCol is available
        Patient? patientDoc = null;
        if (!string.IsNullOrWhiteSpace(patientId) && PatientsCol != null)
        {
            patientDoc = await PatientsCol
                .Find(Builders<Patient>.Filter.Eq(p => p.Id, patientId))
                .FirstOrDefaultAsync(ct);

            if (patientDoc == null && isNewSample)
            {
                throw new PatientNotFoundException(patientId);
            }
        }

        // Validate sample is not already in a terminal/post-collection state that forbids re-recording collection time
        if (sample != null && sample.Status is SampleStatus.Completed or SampleStatus.Rejected)
        {
            throw new InvalidSampleStatusTransitionException(
                sample.Status.ToString(),
                SampleStatus.Collected.ToString(),
                $"Mẫu đã ở trạng thái '{sample.Status}'. Các trạng thái đích hợp lệ: {string.Join(", ", AllowedTransitions[sample.Status].Select(x => x.ToString()))}");
        }

        var collectorId = !string.IsNullOrWhiteSpace(request.CollectedBy)
            ? request.CollectedBy.Trim()
            : operatorUserId;

        var collectorName = !string.IsNullOrWhiteSpace(request.CollectorName)
            ? request.CollectorName.Trim()
            : await ResolveUserFullNameAsync(collectorId, ct);

        if (isNewSample)
        {
            var generatedId = !string.IsNullOrWhiteSpace(request.SampleId)
                ? request.SampleId.Trim()
                : $"SMP-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(1000, 10000)}";

            var generatedBarcode = !string.IsNullOrWhiteSpace(request.Barcode)
                ? request.Barcode.Trim()
                : $"{now:yyMMdd}{RandomNumberGenerator.GetInt32(1000, 10000)}";

            sample = new Sample
            {
                Id = generatedId,
                Barcode = generatedBarcode,
                RfidTag = string.IsNullOrWhiteSpace(request.RfidTag) ? null : request.RfidTag.Trim(),
                TestOrderId = appointmentId ?? $"ORD-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(100, 1000)}",
                PatientId = patientId ?? string.Empty,
                PatientName = patientDoc?.FullName ?? string.Empty,
                PatientGender = patientDoc?.GenderString,
                SampleType = string.IsNullOrWhiteSpace(request.SampleType) ? "Blood" : request.SampleType.Trim(),
                TubeColor = string.IsNullOrWhiteSpace(request.TubeColor) ? "Purple" : request.TubeColor.Trim(),
                Anticoagulant = string.IsNullOrWhiteSpace(request.Anticoagulant) ? "EDTA" : request.Anticoagulant.Trim(),
                VolumeMl = request.VolumeMl ?? 2.0,
                StatusString = SampleStatus.Collected.ToString(),
                PriorityString = string.IsNullOrWhiteSpace(request.Priority) ? SamplePriority.Normal.ToString() : request.Priority.Trim(),
                LabRoom = string.IsNullOrWhiteSpace(request.LabRoom) ? "LAB-ROOM-201" : request.LabRoom.Trim(),
                CollectedBy = collectorId,
                CollectorName = collectorName,
                CollectedAt = collectedAt,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                CreatedAt = now,
                UpdatedAt = now,
                StatusHistory =
                [
                    new SampleStatusHistoryEntry
                    {
                        FromStatus = SampleStatus.PendingCollection.ToString(),
                        ToStatus = SampleStatus.Collected.ToString(),
                        ChangedAt = collectedAt,
                        ChangedBy = collectorId,
                        OperatorName = collectorName,
                        Notes = request.Notes ?? "Ghi nhận thời gian lấy mẫu bệnh phẩm"
                    }
                ]
            };

            if (SamplesCol != null)
            {
                await SamplesCol.InsertOneAsync(sample, cancellationToken: ct);
            }
            _context.Samples.Add(sample);
        }
        else
        {
            var prevStatus = sample!.StatusString;
            sample.CollectedAt = collectedAt;
            if (!string.IsNullOrWhiteSpace(collectorId)) sample.CollectedBy = collectorId;
            if (!string.IsNullOrWhiteSpace(collectorName)) sample.CollectorName = collectorName;
            if (!string.IsNullOrWhiteSpace(request.RfidTag)) sample.RfidTag = request.RfidTag.Trim();
            if (!string.IsNullOrWhiteSpace(request.SampleType)) sample.SampleType = request.SampleType.Trim();
            if (!string.IsNullOrWhiteSpace(request.TubeColor)) sample.TubeColor = request.TubeColor.Trim();
            if (!string.IsNullOrWhiteSpace(request.Anticoagulant)) sample.Anticoagulant = request.Anticoagulant.Trim();
            if (request.VolumeMl is { } vol) sample.VolumeMl = vol;
            if (!string.IsNullOrWhiteSpace(request.Priority)) sample.PriorityString = request.Priority.Trim();
            if (!string.IsNullOrWhiteSpace(request.LabRoom)) sample.LabRoom = request.LabRoom.Trim();
            if (!string.IsNullOrWhiteSpace(request.Notes)) sample.Notes = request.Notes.Trim();
            if (!string.IsNullOrWhiteSpace(patientId)) sample.PatientId = patientId;
            if (!string.IsNullOrWhiteSpace(appointmentId)) sample.TestOrderId = appointmentId;

            if (sample.Status == SampleStatus.PendingCollection)
            {
                sample.Status = SampleStatus.Collected;
            }

            sample.UpdatedAt = now;
            sample.StatusHistory ??= [];
            sample.StatusHistory.Add(new SampleStatusHistoryEntry
            {
                FromStatus = prevStatus,
                ToStatus = sample.StatusString,
                ChangedAt = collectedAt,
                ChangedBy = collectorId,
                OperatorName = collectorName,
                Notes = request.Notes ?? "Ghi nhận thời gian lấy mẫu bệnh phẩm"
            });

            await PersistSampleUpdateAsync(sample, ct);
        }

        // Update linked appointment status to SampleCollected if currently CheckedIn/Pending
        if (!string.IsNullOrWhiteSpace(sample.TestOrderId) && AppointmentsCol != null)
        {
            try
            {
                await AppointmentsCol.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Eq("_id", sample.TestOrderId),
                        Builders<BsonDocument>.Filter.In("status", new[] { "Pending", "Confirmed", "CheckedIn" })),
                    Builders<BsonDocument>.Update.Set("status", "SampleCollected"),
                    cancellationToken: ct);
            }
            catch
            {
                // Non-fatal
            }
        }

        // Append RFID tracking log if RfidTag is present
        if (!string.IsNullOrWhiteSpace(sample.RfidTag) && RfidLogsCol != null)
        {
            try
            {
                await RfidLogsCol.InsertOneAsync(new BsonDocument
                {
                    ["sample_id"] = sample.Id,
                    ["rfid_tag"] = sample.RfidTag,
                    ["scanner_id"] = "RFID-GATE-COLLECTION",
                    ["location"] = "Phlebotomy Collection Area -> Pneumatic Tube",
                    ["direction"] = "OUT",
                    ["scanned_at"] = collectedAt
                }, cancellationToken: ct);
            }
            catch
            {
                // Non-fatal
            }
        }

        _logger?.LogInformation(
            "Sample collection time recorded: {SampleId} (Barcode={Barcode}) at {CollectedAt:O} for Patient={PatientId}",
            sample.Id, sample.Barcode, sample.CollectedAt, sample.PatientId);

        await EnrichSampleMetadataAsync([sample], ct);
        return MapToDto(sample);
    }

    /// <inheritdoc />
    /// <summary>
    /// UC45 - Update Sample Status (Issue I2-076 | BR-137 / ISO 15189 Laboratory Specimen Workflow).
    /// Validates forward-only state transitions (Pending -> Collected -> CheckedIn -> Processing -> Completed),
    /// maintains specimen chain of custody, and updates audit timestamps.
    /// </summary>
    public async Task<LabQueueItemDto> UpdateSampleStatusDetailedAsync(
        string idOrBarcode,
        UpdateSampleStatusDto dto,
        string? operatorUserId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrBarcode))
            throw new SampleNotFoundException(idOrBarcode);

        var sample = await FindSampleAsync(idOrBarcode.Trim(), ct)
                     ?? throw new SampleNotFoundException(idOrBarcode);

        if (string.IsNullOrWhiteSpace(dto.Status) ||
            !Enum.TryParse<SampleStatus>(dto.Status.Trim(), true, out var targetStatus))
        {
            throw new DomainValidationException(
                "INVALID_SAMPLE_STATUS",
                $"Trạng thái mẫu '{dto.Status}' không hợp lệ. Các trạng thái hỗ trợ: PendingCollection, Collected, InTransit, Received, Processing, Testing, Completed, Rejected, RerunRequested.");
        }

        var currentStatus = sample.Status;
        var allowed = AllowedTransitions.TryGetValue(currentStatus, out var nextStates) ? nextStates : [];

        // Validate permitted status transition (allow idempotent update if same status or Processing <-> Testing)
        if (currentStatus != targetStatus && !allowed.Contains(targetStatus))
        {
            throw new InvalidSampleStatusTransitionException(
                sample.StatusString,
                targetStatus.ToString(),
                $"Các trạng thái đích hợp lệ từ '{sample.StatusString}': {string.Join(", ", allowed.Select(s => s.ToString()))}");
        }

        // Rejection requires a non-empty clinical rejection reason (ISO 15189 pre-analytical control)
        if (targetStatus == SampleStatus.Rejected &&
            string.IsNullOrWhiteSpace(dto.RejectionReason) &&
            string.IsNullOrWhiteSpace(sample.RejectionReason))
        {
            throw new DomainValidationException(
                "REJECTION_REASON_REQUIRED",
                "Khi từ chối mẫu bệnh phẩm (Rejected), bắt buộc phải cung cấp lý do từ chối (RejectionReason) như: Mẫu vỡ hồng cầu (Hemolysis), Mẫu đông (Clotted), Không đủ thể tích.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var transitionTime = dto.Timestamp.HasValue
            ? (dto.Timestamp.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dto.Timestamp.Value, DateTimeKind.Utc)
                : dto.Timestamp.Value.ToUniversalTime())
            : now;

        if (transitionTime > now.AddMinutes(2))
        {
            throw new InvalidSampleTimestampException(
                "Thời điểm cập nhật trạng thái mẫu không được ở tương lai.");
        }

        if (targetStatus != SampleStatus.PendingCollection &&
            targetStatus != SampleStatus.Collected &&
            transitionTime < sample.CollectedAt.AddMinutes(-1))
        {
            throw new InvalidSampleTimestampException(
                $"Thời điểm chuyển trạng thái ({transitionTime:yyyy-MM-dd HH:mm:ss} UTC) không được nhỏ hơn thời điểm lấy mẫu ({sample.CollectedAt:yyyy-MM-dd HH:mm:ss} UTC).");
        }

        var prevStatusString = sample.StatusString;
        // Preserve exact string if user passed "Testing" vs "Processing"
        sample.StatusString = dto.Status.Trim().Equals("Testing", StringComparison.OrdinalIgnoreCase)
            ? "Testing"
            : targetStatus.ToString();

        var techId = !string.IsNullOrWhiteSpace(dto.TechnicianId) ? dto.TechnicianId.Trim() : operatorUserId;
        var techName = !string.IsNullOrWhiteSpace(dto.TechnicianName)
            ? dto.TechnicianName.Trim()
            : await ResolveUserFullNameAsync(techId, ct);

        switch (targetStatus)
        {
            case SampleStatus.Collected:
                sample.CollectedAt = transitionTime;
                if (!string.IsNullOrWhiteSpace(techId)) sample.CollectedBy = techId;
                if (!string.IsNullOrWhiteSpace(techName)) sample.CollectorName = techName;
                break;

            case SampleStatus.InTransit:
                sample.InTransitAt = transitionTime;
                break;

            case SampleStatus.Received:
                sample.ReceivedAt = transitionTime;
                if (!string.IsNullOrWhiteSpace(techId)) sample.ReceivedBy = techId;
                if (!string.IsNullOrWhiteSpace(techName)) sample.AssignedTechnicianName = techName;
                break;

            case SampleStatus.Processing:
            case SampleStatus.Testing:
                sample.ReceivedAt ??= transitionTime;
                sample.ProcessedAt = transitionTime;
                if (!string.IsNullOrWhiteSpace(techId)) sample.AssignedTechnicianId = techId;
                if (!string.IsNullOrWhiteSpace(techName)) sample.AssignedTechnicianName = techName;
                break;

            case SampleStatus.Completed:
                sample.ReceivedAt ??= sample.CollectedAt;
                sample.ProcessedAt ??= transitionTime;
                sample.CompletedAt = transitionTime;
                if (!string.IsNullOrWhiteSpace(techId)) sample.AssignedTechnicianId = techId;
                if (!string.IsNullOrWhiteSpace(techName)) sample.AssignedTechnicianName = techName;
                break;

            case SampleStatus.Rejected:
                sample.RejectedAt = transitionTime;
                if (!string.IsNullOrWhiteSpace(dto.RejectionReason))
                    sample.RejectionReason = dto.RejectionReason.Trim();
                break;

            case SampleStatus.RerunRequested:
                sample.Priority = SamplePriority.Stat;
                if (!string.IsNullOrWhiteSpace(dto.Notes))
                    sample.RerunReason = dto.Notes.Trim();
                break;
        }

        if (!string.IsNullOrWhiteSpace(dto.LabRoom))
            sample.LabRoom = dto.LabRoom.Trim();

        if (!string.IsNullOrWhiteSpace(dto.Notes))
            sample.Notes = dto.Notes.Trim();

        sample.UpdatedAt = now;
        sample.StatusHistory ??= [];
        sample.StatusHistory.Add(new SampleStatusHistoryEntry
        {
            FromStatus = prevStatusString,
            ToStatus = sample.StatusString,
            ChangedAt = transitionTime,
            ChangedBy = techId,
            OperatorName = techName,
            Notes = dto.RejectionReason ?? dto.Notes
        });

        await PersistSampleUpdateAsync(sample, ct);

        _logger?.LogInformation(
            "Sample {SampleId} status transitioned: {From} -> {To} at {Time:O} by {Tech}",
            sample.Id, prevStatusString, sample.StatusString, transitionTime, techName ?? techId ?? "SYSTEM");

        await EnrichSampleMetadataAsync([sample], ct);
        return MapToDto(sample);
    }

    public async Task<bool> UpdateStatusAsync(string id, UpdateSampleStatusDto dto)
    {
        try
        {
            await UpdateSampleStatusDetailedAsync(id, dto);
            return true;
        }
        catch (SampleNotFoundException)
        {
            return false;
        }
    }

    public async Task<bool> RequestRerunAsync(string id, SampleRerunRequestDto dto)
    {
        var sample = await FindSampleAsync(id);
        if (sample == null)
            return false;

        var now = _clock.GetUtcNow().UtcDateTime;
        var prev = sample.StatusString;
        sample.Status = SampleStatus.RerunRequested;
        sample.Priority = SamplePriority.Stat;
        sample.RerunReason = dto.Reason.Trim();
        sample.UpdatedAt = now;

        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            sample.Notes = string.IsNullOrWhiteSpace(sample.Notes)
                ? $"Yêu cầu chạy lại bởi {dto.RequestedBy}: {dto.Reason} ({dto.Notes})"
                : $"{sample.Notes} | Yêu cầu chạy lại bởi {dto.RequestedBy}: {dto.Reason}";
        }

        sample.StatusHistory ??= [];
        sample.StatusHistory.Add(new SampleStatusHistoryEntry
        {
            FromStatus = prev,
            ToStatus = SampleStatus.RerunRequested.ToString(),
            ChangedAt = now,
            OperatorName = dto.RequestedBy,
            Notes = dto.Reason
        });

        await PersistSampleUpdateAsync(sample);
        return true;
    }

    private async Task<(string? paramName, string? value, string? flag, string? analyzer, List<string> availableParams)> ResolveSampleTestResultDetailsAsync(Sample s)
    {
        if (TestResultsCol != null)
        {
            try
            {
                var tr = await TestResultsCol.Find(r => r.SampleId == s.Id || r.SampleId == s.Barcode || (!string.IsNullOrEmpty(s.TestOrderId) && r.SampleId == s.TestOrderId)).FirstOrDefaultAsync();
                if (tr != null && tr.Items != null && tr.Items.Count > 0)
                {
                    var targetItem = tr.Items.FirstOrDefault(i => i.Flag != ResultFlag.Normal) ?? tr.Items.First();
                    var flagStr = targetItem.Flag switch
                    {
                        ResultFlag.CriticalPanic => "CRITICAL",
                        ResultFlag.High => "H",
                        ResultFlag.Low => "L",
                        _ => string.Empty
                    };
                    var available = tr.Items.Select(i => i.CriterionName).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
                    return (targetItem.CriterionName, targetItem.MeasuredValue, flagStr, s.LabRoom, available);
                }
            }
            catch { }
        }

        return (null, null, null, s.LabRoom, new List<string>());
    }

    public async Task<List<SampleRerunItemDto>> GetRerunRequestsAsync()
    {
        var result = new List<SampleRerunItemDto>();

        if (SamplesCol != null)
        {
            try
            {
                var fb = Builders<Sample>.Filter;
                var dbReruns = await SamplesCol
                    .Find(fb.Eq(s => s.Status, SampleStatus.RerunRequested))
                    .ToListAsync();

                foreach (var s in dbReruns)
                {
                    var (paramName, originalVal, _, _, _) = await ResolveSampleTestResultDetailsAsync(s);
                    result.Add(new SampleRerunItemDto
                    {
                        SpecimenId = !string.IsNullOrEmpty(s.Barcode) ? s.Barcode : s.Id,
                        PatientName = s.PatientName,
                        Parameter = paramName ?? string.Empty,
                        Reason = s.RerunReason ?? string.Empty,
                        Status = "In progress",
                        OriginalValue = originalVal ?? string.Empty,
                        RerunValue = null,
                        RequestedAt = s.UpdatedAt ?? s.CollectedAt
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to query blood_samples for reruns in MongoDB");
            }
        }

        lock (_context.Samples)
        {
            foreach (var s in _context.Samples.Where(s => s.Status == SampleStatus.RerunRequested))
            {
                var specId = !string.IsNullOrEmpty(s.Barcode) ? s.Barcode : s.Id;
                if (result.Any(r => r.SpecimenId == specId)) continue;

                result.Add(new SampleRerunItemDto
                {
                    SpecimenId = specId,
                    PatientName = s.PatientName,
                    Parameter = string.Empty,
                    Reason = s.RerunReason ?? string.Empty,
                    Status = "New",
                    OriginalValue = string.Empty,
                    RerunValue = null,
                    RequestedAt = s.UpdatedAt ?? s.CollectedAt
                });
            }
        }

        return result;
    }

    public async Task<SampleRerunDetailsDto?> GetSampleRerunDetailsAsync(string id)
    {
        var sample = await FindSampleAsync(id);
        if (sample == null)
        {
            return null;
        }

        var (paramName, originalVal, flagStr, analyzer, availableParams) = await ResolveSampleTestResultDetailsAsync(sample);

        string currentResult = !string.IsNullOrEmpty(paramName) && !string.IsNullOrEmpty(originalVal)
            ? $"{paramName} = {originalVal}"
            : string.Empty;

        return new SampleRerunDetailsDto
        {
            SampleId = sample.Id,
            Barcode = sample.Barcode,
            PatientId = sample.PatientId,
            PatientName = sample.PatientName,
            CurrentResult = currentResult,
            Flag = flagStr ?? string.Empty,
            Analyzer = analyzer ?? string.Empty,
            FirstRunTime = sample.CollectedAt != default ? sample.CollectedAt.ToString("HH:mm") : string.Empty,
            AvailableParameters = availableParams
        };
    }

    private async Task<Sample?> FindSampleAsync(string idOrBarcode, CancellationToken ct = default)
    {
        if (SamplesCol != null)
        {
            try
            {
                var fb = Builders<Sample>.Filter;
                var dbSample = await SamplesCol
                    .Find(fb.Or(
                        fb.Eq(s => s.Id, idOrBarcode),
                        fb.Eq(s => s.Barcode, idOrBarcode),
                        fb.Eq(s => s.RfidTag, idOrBarcode)))
                    .FirstOrDefaultAsync(ct);

                if (dbSample != null)
                    return dbSample;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to query blood_samples in MongoDB for {Key}", idOrBarcode);
            }
        }

        return _context.Samples.FirstOrDefault(x =>
            x.Id.Equals(idOrBarcode, StringComparison.OrdinalIgnoreCase) ||
            x.Barcode.Equals(idOrBarcode, StringComparison.OrdinalIgnoreCase));
    }

    private async Task PersistSampleUpdateAsync(Sample sample, CancellationToken ct = default)
    {
        if (SamplesCol != null)
        {
            try
            {
                await SamplesCol.ReplaceOneAsync(
                    Builders<Sample>.Filter.Eq(s => s.Id, sample.Id),
                    sample,
                    new ReplaceOptions { IsUpsert = true },
                    ct);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to persist sample {SampleId} to MongoDB", sample.Id);
            }
        }

        var memIdx = _context.Samples.FindIndex(x => x.Id.Equals(sample.Id, StringComparison.OrdinalIgnoreCase));
        if (memIdx >= 0)
        {
            _context.Samples[memIdx] = sample;
        }
    }

    private async Task<List<Sample>> LoadAllSamplesEnrichedAsync(CancellationToken ct = default)
    {
        var byId = new Dictionary<string, Sample>(StringComparer.OrdinalIgnoreCase);

        if (SamplesCol != null)
        {
            try
            {
                var mongoSamples = await SamplesCol.Find(FilterDefinition<Sample>.Empty).ToListAsync(ct);
                foreach (var s in mongoSamples)
                {
                    byId[s.Id] = s;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not load blood_samples from MongoDB; using BmsDbContext fallback.");
            }
        }

        foreach (var memSample in _context.Samples)
        {
            byId.TryAdd(memSample.Id, memSample);
        }

        var list = byId.Values.ToList();
        await EnrichSampleMetadataAsync(list, ct);
        return list;
    }

    private async Task EnrichSampleMetadataAsync(IReadOnlyList<Sample> samples, CancellationToken ct = default)
    {
        if (samples.Count == 0 || _db == null)
            return;

        try
        {
            var missingPatients = samples
                .Where(s => string.IsNullOrWhiteSpace(s.PatientName) && !string.IsNullOrWhiteSpace(s.PatientId))
                .Select(s => s.PatientId)
                .Distinct()
                .ToList();

            if (missingPatients.Count > 0 && PatientsCol != null)
            {
                var patients = await PatientsCol
                    .Find(Builders<Patient>.Filter.In(p => p.Id, missingPatients))
                    .ToListAsync(ct);
                var pMap = patients.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

                var nowYear = _clock.GetUtcNow().Year;
                foreach (var s in samples)
                {
                    if (pMap.TryGetValue(s.PatientId, out var p))
                    {
                        if (string.IsNullOrWhiteSpace(s.PatientName)) s.PatientName = p.FullName;
                        s.PatientGender ??= p.GenderString;
                        if (s.PatientAge == null && p.DateOfBirth.HasValue)
                            s.PatientAge = Math.Max(0, nowYear - p.DateOfBirth.Value.Year);
                    }
                }
            }

            var collectorIds = samples
                .Where(s => string.IsNullOrWhiteSpace(s.CollectorName) && !string.IsNullOrWhiteSpace(s.CollectedBy))
                .Select(s => s.CollectedBy!)
                .Distinct()
                .ToList();

            if (collectorIds.Count > 0 && AccountsCol != null)
            {
                var users = await AccountsCol
                    .Find(Builders<User>.Filter.In(u => u.Id, collectorIds))
                    .ToListAsync(ct);
                var uMap = users.ToDictionary(u => u.Id, u => u.FullName, StringComparer.OrdinalIgnoreCase);

                foreach (var s in samples)
                {
                    if (string.IsNullOrWhiteSpace(s.CollectorName) &&
                        !string.IsNullOrWhiteSpace(s.CollectedBy) &&
                        uMap.TryGetValue(s.CollectedBy, out var cName))
                    {
                        s.CollectorName = cName;
                    }
                }
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    private async Task<string?> ResolveUserFullNameAsync(string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || AccountsCol == null)
            return null;

        try
        {
            var user = await AccountsCol.Find(u => u.Id == userId.Trim()).FirstOrDefaultAsync(ct);
            return user?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static LabQueueItemDto MapToDto(Sample s) => new()
    {
        Id = s.Id,
        Barcode = s.Barcode,
        RfidTag = s.RfidTag,
        TestOrderId = s.TestOrderId,
        AppointmentId = s.TestOrderId,
        PatientId = s.PatientId,
        PatientName = s.PatientName,
        PatientGender = s.PatientGender,
        PatientAge = s.PatientAge,
        TestPackageName = s.TestPackageName,
        SampleType = s.SampleType,
        TubeColor = s.TubeColor,
        Anticoagulant = s.Anticoagulant,
        VolumeMl = s.VolumeMl,
        Status = s.StatusString,
        Priority = s.PriorityString,
        LabRoom = s.LabRoom,
        CollectedBy = s.CollectedBy,
        CollectorName = s.CollectorName,
        AssignedTechnicianId = s.AssignedTechnicianId,
        AssignedTechnicianName = s.AssignedTechnicianName,
        RejectionReason = s.RejectionReason,
        RerunReason = s.RerunReason,
        CollectedAt = s.CollectedAt,
        InTransitAt = s.InTransitAt,
        ReceivedAt = s.ReceivedAt,
        ProcessedAt = s.ProcessedAt,
        CompletedAt = s.CompletedAt,
        RejectedAt = s.RejectedAt,
        Notes = s.Notes,
        StatusHistory = (s.StatusHistory ?? []).Select(h => new SampleStatusHistoryDto
        {
            FromStatus = h.FromStatus,
            ToStatus = h.ToStatus,
            ChangedAt = h.ChangedAt,
            ChangedBy = h.ChangedBy,
            OperatorName = h.OperatorName,
            Notes = h.Notes
        }).ToList()
    };
}
