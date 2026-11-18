using System.Security.Cryptography;
using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
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
    /// Permitted ISO 15189 specimen lifecycle state machine transitions (Strict Forward-Only).
    /// Prevents skipping stages (nhảy cóc) and prevents invalid backward loops.
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
                SampleStatus.InTransit,
                SampleStatus.Received,
                SampleStatus.Rejected
            ],
            [SampleStatus.InTransit] =
            [
                SampleStatus.Received,
                SampleStatus.Rejected
            ],
            [SampleStatus.Received] =
            [
                SampleStatus.Processing,
                SampleStatus.Testing,
                SampleStatus.Rejected
            ],
            [SampleStatus.Processing] =
            [
                SampleStatus.Testing,
                SampleStatus.Rejected
            ],
            [SampleStatus.Testing] =
            [
                SampleStatus.Completed,
                SampleStatus.RerunRequested,
                SampleStatus.Rejected
            ],
            [SampleStatus.RerunRequested] =
            [
                SampleStatus.Testing,
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
    private IMongoCollection<BsonDocument>? RawAccountsCol => _db?.GetCollection<BsonDocument>("accounts");
    private IMongoCollection<BsonDocument>? LabUserProfilesCol => _db?.GetCollection<BsonDocument>("lab_user_profiles");
    private IMongoCollection<BsonDocument>? DoctorsCol => _db?.GetCollection<BsonDocument>("doctors");
    private IMongoCollection<BsonDocument>? RfidLogsCol => _db?.GetCollection<BsonDocument>("rfid_tracking_logs");
    private IMongoCollection<TestResult>? TestResultsCol => _db?.GetCollection<TestResult>("test_results");
    private IMongoCollection<AuditLog>? AuditLogsCol => _db?.GetCollection<AuditLog>("audit_logs");
    private IMongoCollection<BsonDocument>? TestOrdersCol => _db?.GetCollection<BsonDocument>("test_orders");
    private IMongoCollection<BsonDocument>? ServicePackagesCol => _db?.GetCollection<BsonDocument>("service_packages");
    private IMongoCollection<BsonDocument>? TestPackagesCol => _db?.GetCollection<BsonDocument>("test_packages");
    private IMongoCollection<BsonDocument>? TestPackageCriteriaCol => _db?.GetCollection<BsonDocument>("test_package_criteria");
    private IMongoCollection<BsonDocument>? TestCriteriaCol => _db?.GetCollection<BsonDocument>("test_criteria");

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

                if (appointmentDoc.TryGetValue("created_at", out var appCreatedVal) && appCreatedVal.IsValidDateTime)
                {
                    var appCreatedUtc = appCreatedVal.ToUniversalTime();
                    if (collectedAt < appCreatedUtc.AddMinutes(-5))
                    {
                        throw new InvalidSampleTimestampException(
                            $"Thời điểm lấy mẫu ({collectedAt:yyyy-MM-dd HH:mm:ss} UTC) không hợp lý so với thời điểm tạo đơn/lịch hẹn ({appCreatedUtc:yyyy-MM-dd HH:mm:ss} UTC).");
                    }
                }

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

        if (!string.IsNullOrWhiteSpace(appointmentId) && TestOrdersCol != null)
        {
            var orderDoc = await TestOrdersCol
                .Find(Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("_id", appointmentId),
                    Builders<BsonDocument>.Filter.Eq("order_id", appointmentId),
                    Builders<BsonDocument>.Filter.Eq("appointment_id", appointmentId)))
                .FirstOrDefaultAsync(ct);

            if (orderDoc != null && orderDoc.TryGetValue("created_at", out var ordCreatedVal) && ordCreatedVal.IsValidDateTime)
            {
                var ordCreatedUtc = ordCreatedVal.ToUniversalTime();
                if (collectedAt < ordCreatedUtc.AddMinutes(-5))
                {
                    throw new InvalidSampleTimestampException(
                        $"Thời điểm lấy mẫu ({collectedAt:yyyy-MM-dd HH:mm:ss} UTC) không hợp lý so với thời điểm tạo đơn xét nghiệm ({ordCreatedUtc:yyyy-MM-dd HH:mm:ss} UTC).");
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

        // Validate sample is not already in a post-collection or processing state (Record Sample Time is only valid for PendingCollection)
        if (sample != null && sample.Status != SampleStatus.PendingCollection)
        {
            throw new InvalidSampleStatusTransitionException(
                sample.StatusString,
                SampleStatus.Collected.ToString(),
                $"Mẫu '{sample.Barcode}' đã được ghi nhận lấy mẫu lúc {sample.CollectedAt:dd/MM/yyyy HH:mm} và đang ở trạng thái '{sample.StatusString}'. Không thể ghi nhận lại thời gian lấy mẫu cho mẫu đã chuyển vào quy trình Lab.");
        }

        var collectorId = !string.IsNullOrWhiteSpace(request.CollectedBy)
            ? request.CollectedBy.Trim()
            : operatorUserId;

        var collectorName = !string.IsNullOrWhiteSpace(request.CollectorName)
            ? request.CollectorName.Trim()
            : await ResolveUserFullNameAsync(collectorId, ct);

        if (isNewSample)
        {
            // Auto-link the patient's latest active appointment or test order when caller omitted AppointmentId.
            if (string.IsNullOrWhiteSpace(appointmentId) && !string.IsNullOrWhiteSpace(patientId))
            {
                var patientKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { patientId };
                if (!string.IsNullOrWhiteSpace(patientDoc?.PatientCode))
                    patientKeys.Add(patientDoc.PatientCode);

                if (AppointmentsCol != null)
                {
                    try
                    {
                        var af = Builders<BsonDocument>.Filter;
                        var activeAppt = await AppointmentsCol.Find(af.And(
                                af.Or(af.In("patient_id", patientKeys), af.In("patientId", patientKeys), af.In("PatientId", patientKeys)),
                                af.Nin("status", new BsonValue[] { "Cancelled", "cancelled", 5 }),
                                af.Nin("Status", new BsonValue[] { "Cancelled", "cancelled", 5 })))
                            .Sort(Builders<BsonDocument>.Sort.Descending("createdAt").Descending("created_at").Descending("CreatedAt"))
                            .FirstOrDefaultAsync(ct);

                        if (activeAppt != null)
                        {
                            appointmentId = ExtractBsonId(activeAppt);
                        }
                    }
                    catch
                    {
                        // Non-fatal
                    }
                }

                if (string.IsNullOrWhiteSpace(appointmentId) && TestOrdersCol != null)
                {
                    try
                    {
                        var of = Builders<BsonDocument>.Filter;
                        var activeOrder = await TestOrdersCol.Find(of.And(
                                of.Or(of.In("patient_id", patientKeys), of.In("patientId", patientKeys), of.In("PatientId", patientKeys)),
                                of.Nin("status", new BsonValue[] { "Cancelled", "cancelled" }),
                                of.Nin("Status", new BsonValue[] { "Cancelled", "cancelled" })))
                            .Sort(Builders<BsonDocument>.Sort.Descending("createdAt").Descending("created_at").Descending("CreatedAt"))
                            .FirstOrDefaultAsync(ct);

                        if (activeOrder != null)
                        {
                            var ordNum = GetBsonString(activeOrder, "order_number", "OrderNumber", "order_id");
                            appointmentId = !string.IsNullOrWhiteSpace(ordNum) ? ordNum : ExtractBsonId(activeOrder);
                        }
                    }
                    catch
                    {
                        // Non-fatal
                    }
                }
            }

            var generatedBarcode = !string.IsNullOrWhiteSpace(request.Barcode)
                ? request.Barcode.Trim()
                : $"SMP-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(100000, 1000000)}";

            // Unify SampleId with Barcode when Barcode already uses the canonical SMP-... format
            // so the same tube never displays two different SMP-... codes across Lab, Doctor, and Patient screens.
            var generatedId = !string.IsNullOrWhiteSpace(request.SampleId)
                ? request.SampleId.Trim()
                : (generatedBarcode.StartsWith("SMP-", StringComparison.OrdinalIgnoreCase)
                    ? generatedBarcode
                    : $"SMP-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(1000, 10000)}");

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

            if (AuditLogsCol != null)
            {
                try
                {
                    await AuditLogsCol.InsertOneAsync(new AuditLog
                    {
                        AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                        Timestamp = now,
                        UserId = operatorUserId ?? collectorId ?? "ANONYMOUS",
                        UserName = collectorName ?? operatorUserId ?? "ANONYMOUS",
                        UserRole = "Lab",
                        Action = "RECORD_SAMPLE_COLLECTION_TIME",
                        Category = "SPECIMEN_TRACKING",
                        Severity = "INFO",
                        Status = "SUCCESS",
                        IpAddress = "127.0.0.1",
                        TargetEntity = "Sample",
                        EntityId = sample.Id,
                        Details = $"Recorded initial collection time for sample {sample.Id} (Barcode: {sample.Barcode}, Patient: {sample.PatientId}) at {collectedAt:yyyy-MM-dd HH:mm:ss} UTC. System CreatedAt: {now:yyyy-MM-dd HH:mm:ss} UTC."
                    }, cancellationToken: ct);
                }
                catch { /* Skip audit failure */ }
            }
        }
        else
        {
            var oldCollectedAt = sample!.CollectedAt;
            var prevStatus = sample.StatusString;
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

            if (AuditLogsCol != null)
            {
                try
                {
                    await AuditLogsCol.InsertOneAsync(new AuditLog
                    {
                        AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                        Timestamp = now,
                        UserId = operatorUserId ?? collectorId ?? "ANONYMOUS",
                        UserName = collectorName ?? operatorUserId ?? "ANONYMOUS",
                        UserRole = "Lab",
                        Action = "UPDATE_SAMPLE_COLLECTION_TIME",
                        Category = "SPECIMEN_TRACKING",
                        Severity = "INFO",
                        Status = "SUCCESS",
                        IpAddress = "127.0.0.1",
                        TargetEntity = "Sample",
                        EntityId = sample.Id,
                        Details = $"Updated collection time for sample {sample.Id} (Barcode: {sample.Barcode}) from {oldCollectedAt:yyyy-MM-dd HH:mm:ss} UTC to {collectedAt:yyyy-MM-dd HH:mm:ss} UTC. System UpdatedAt: {now:yyyy-MM-dd HH:mm:ss} UTC."
                    }, cancellationToken: ct);
                }
                catch { /* Skip audit failure */ }
            }
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

        // Validate permitted status transition (strictly forward-only; reject same-status loops)
        if (currentStatus == targetStatus)
        {
            throw new InvalidSampleStatusTransitionException(
                sample.StatusString,
                targetStatus.ToString(),
                $"Mẫu '{sample.Barcode}' hiện đã ở trạng thái '{sample.StatusString}'. Vui lòng chọn trạng thái chuyển tiếp tiếp theo: {string.Join(", ", allowed.Select(s => s.ToString()))}.");
        }

        if (!allowed.Contains(targetStatus))
        {
            throw new InvalidSampleStatusTransitionException(
                sample.StatusString,
                targetStatus.ToString(),
                $"Không thể chuyển trạng thái mẫu từ '{sample.StatusString}' sang '{targetStatus}'. Các trạng thái hợp lệ theo ISO 15189: {string.Join(", ", allowed.Select(s => s.ToString()))}.");
        }

        // Rejection requires a non-empty clinical rejection reason (ISO 15189 pre-analytical control)
        var rejectionReason = !string.IsNullOrWhiteSpace(dto.RejectionReason)
            ? dto.RejectionReason.Trim()
            : (!string.IsNullOrWhiteSpace(dto.Notes) ? dto.Notes.Trim() : null);

        // Rejection requires a non-empty clinical rejection reason (ISO 15189 pre-analytical control)
        if (targetStatus == SampleStatus.Rejected && string.IsNullOrWhiteSpace(rejectionReason))
        {
            throw new DomainValidationException(
                "REJECTION_REASON_REQUIRED",
                "Khi từ chối mẫu bệnh phẩm (Rejected), bắt buộc phải cung cấp lý do từ chối (RejectionReason) như: Mẫu vỡ hồng cầu (Hemolysis), Mẫu đông (Clotted), Không đủ thể tích, Sai định danh ống nghiệm.");
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
                if (!string.IsNullOrWhiteSpace(techId))
                {
                    sample.ReceivedBy = techId;
                    sample.AssignedTechnicianId = techId;
                }
                if (!string.IsNullOrWhiteSpace(techName)) sample.AssignedTechnicianName = techName;
                if (!string.IsNullOrWhiteSpace(dto.HandoverBy)) sample.HandoverBy = dto.HandoverBy.Trim();
                if (!string.IsNullOrWhiteSpace(dto.HandoverById)) sample.HandoverById = dto.HandoverById.Trim();
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
                sample.RejectionReason = rejectionReason;
                sample.RejectedBy = techId;
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
            Notes = rejectionReason ?? dto.Notes
        });

        await PersistSampleUpdateAsync(sample, ct);

        // ISO 27789 Audit Trail Logging for specimen lifecycle transition
        if (AuditLogsCol != null)
        {
            try
            {
                var action = targetStatus == SampleStatus.Rejected
                    ? "REJECT_SAMPLE"
                    : "UPDATE_SAMPLE_STATUS";
                var severity = targetStatus == SampleStatus.Rejected
                    ? "WARNING"
                    : "INFO";
                var handoverSuffix = targetStatus == SampleStatus.Received && !string.IsNullOrWhiteSpace(sample.HandoverBy)
                    ? $" (Người bàn giao: {sample.HandoverBy})"
                    : string.Empty;
                var details = targetStatus == SampleStatus.Rejected
                    ? $"Mẫu {sample.Id} (Barcode: {sample.Barcode}) bị TỪ CHỐI bởi {techName ?? techId}. Lý do: '{rejectionReason}'. Trạng thái trước: {prevStatusString}."
                    : $"Cập nhật trạng thái mẫu {sample.Id} (Barcode: {sample.Barcode}) từ '{prevStatusString}' sang '{sample.StatusString}' bởi {techName ?? techId}{handoverSuffix}.";

                await AuditLogsCol.InsertOneAsync(new AuditLog
                {
                    AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = now,
                    UserId = techId ?? operatorUserId ?? "ANONYMOUS",
                    UserName = techName ?? operatorUserId ?? "ANONYMOUS",
                    UserRole = "Lab",
                    Action = action,
                    Category = "SPECIMEN_TRACKING",
                    Severity = severity,
                    Status = "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "Sample",
                    EntityId = sample.Id,
                    Details = details
                }, cancellationToken: ct);
            }
            catch { /* Skip audit failure */ }
        }

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

        // Only fallback to in-memory store if MongoDB has no rerun requests
        if (result.Count == 0)
        {
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

    /// <inheritdoc />
    public async Task<List<LabStaffMemberDto>> GetStaffDirectoryAsync(CancellationToken ct = default)
    {
        var staffById = new Dictionary<string, LabStaffMemberDto>(StringComparer.OrdinalIgnoreCase);

        if (RawAccountsCol != null)
        {
            try
            {
                var labProfileMap = new Dictionary<string, (string FullName, string? AssignedRoom)>(StringComparer.OrdinalIgnoreCase);
                if (LabUserProfilesCol != null)
                {
                    var profiles = await LabUserProfilesCol.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct);
                    foreach (var p in profiles)
                    {
                        var pid = ExtractBsonId(p);
                        if (string.IsNullOrWhiteSpace(pid)) continue;
                        var pName = GetBsonString(p, "full_name", "fullName", "FullName");
                        var pRoom = GetBsonString(p, "assigned_room", "assignedRoom", "AssignedRoom");
                        labProfileMap[pid] = (pName, string.IsNullOrWhiteSpace(pRoom) ? null : pRoom);
                    }
                }

                var doctorMap = new Dictionary<string, (string FullName, string? Specialty, string? RoomId)>(StringComparer.OrdinalIgnoreCase);
                if (DoctorsCol != null)
                {
                    var doctors = await DoctorsCol.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct);
                    foreach (var d in doctors)
                    {
                        var did = ExtractBsonId(d);
                        if (string.IsNullOrWhiteSpace(did)) continue;
                        var dName = GetBsonString(d, "full_name", "fullName", "FullName");
                        var dSpec = GetBsonString(d, "specialty", "Specialty");
                        var dRoom = GetBsonString(d, "room_id", "roomId", "RoomId");
                        doctorMap[did] = (dName, string.IsNullOrWhiteSpace(dSpec) ? null : dSpec, string.IsNullOrWhiteSpace(dRoom) ? null : dRoom);
                    }
                }

                var fb = Builders<BsonDocument>.Filter;
                var activeFilter = fb.And(
                    fb.Ne("isDeleted", true),
                    fb.Ne("is_deleted", true),
                    fb.Ne("isActive", false),
                    fb.Ne("Status", 0),
                    fb.Ne("status", 0));

                var accountDocs = await RawAccountsCol.Find(activeFilter).ToListAsync(ct);

                foreach (var doc in accountDocs)
                {
                    var id = ExtractBsonId(doc);
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    var username = GetBsonString(doc, "Username", "username");
                    var fullName = GetBsonString(doc, "FullName", "fullName", "full_name");
                    var storedEmpCode = GetBsonString(doc, "employeeCode", "EmployeeCode", "employee_code");
                    var storedDepartment = GetBsonString(doc, "department", "Department", "specialty", "Specialty");

                    var canonicalRole = ResolveCanonicalStaffRole(doc, id, username, labProfileMap, doctorMap);
                    if (canonicalRole == null || string.Equals(canonicalRole, RoleNames.Patient, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? assignedRoom = null;
                    if (labProfileMap.TryGetValue(id, out var labProf))
                    {
                        if (string.IsNullOrWhiteSpace(fullName)) fullName = labProf.FullName;
                        assignedRoom = labProf.AssignedRoom;
                    }
                    else if (doctorMap.TryGetValue(id, out var docProf))
                    {
                        if (string.IsNullOrWhiteSpace(fullName)) fullName = docProf.FullName;
                        if (string.IsNullOrWhiteSpace(storedDepartment)) storedDepartment = docProf.Specialty ?? string.Empty;
                        assignedRoom = docProf.RoomId;
                    }

                    if (string.IsNullOrWhiteSpace(fullName))
                        fullName = username;
                    if (string.IsNullOrWhiteSpace(fullName))
                        continue;

                    var empCode = !string.IsNullOrWhiteSpace(storedEmpCode)
                        ? storedEmpCode
                        : BuildDefaultEmployeeCode(canonicalRole, id, username);

                    var department = !string.IsNullOrWhiteSpace(storedDepartment)
                        ? storedDepartment
                        : ResolveDefaultDepartment(canonicalRole);

                    staffById[id] = new LabStaffMemberDto
                    {
                        Id = id,
                        EmployeeCode = empCode,
                        Username = username,
                        FullName = fullName,
                        Role = canonicalRole,
                        Department = department,
                        AssignedRoom = assignedRoom
                    };
                }

                // Ensure lab_user_profiles entries appear even if their account document is missing.
                foreach (var (pid, prof) in labProfileMap)
                {
                    if (staffById.ContainsKey(pid) || string.IsNullOrWhiteSpace(prof.FullName)) continue;
                    staffById[pid] = new LabStaffMemberDto
                    {
                        Id = pid,
                        EmployeeCode = BuildDefaultEmployeeCode(RoleNames.Lab, pid, pid),
                        Username = pid,
                        FullName = prof.FullName,
                        Role = RoleNames.Lab,
                        Department = "Khoa Xét Nghiệm",
                        AssignedRoom = prof.AssignedRoom
                    };
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to query staff directory from MongoDB accounts collection.");
            }
        }

        // In-memory fallback only when MongoDB is unavailable (unit tests / offline dev).
        if (staffById.Count == 0 && _db == null)
        {
            foreach (var u in _context.Users.Where(u => u.IsActive && u.Role != UserRole.Patient))
            {
                var roleName = RoleNames.FromRoleId(u.RoleId) ?? u.Role.ToString();
                staffById[u.Id] = new LabStaffMemberDto
                {
                    Id = u.Id,
                    EmployeeCode = !string.IsNullOrWhiteSpace(u.EmployeeCode)
                        ? u.EmployeeCode
                        : BuildDefaultEmployeeCode(roleName, u.Id, u.Username),
                    Username = u.Username,
                    FullName = !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : u.Username,
                    Role = roleName,
                    Department = !string.IsNullOrWhiteSpace(u.Department)
                        ? u.Department
                        : ResolveDefaultDepartment(roleName)
                };
            }
        }

        return staffById.Values
            .OrderBy(s => GetRoleDisplayOrder(s.Role))
            .ThenBy(s => s.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ResolveCanonicalStaffRole(
        BsonDocument doc,
        string id,
        string username,
        IReadOnlyDictionary<string, (string FullName, string? AssignedRoom)> labProfiles,
        IReadOnlyDictionary<string, (string FullName, string? Specialty, string? RoomId)> doctors)
    {
        // Prefer explicit role string on the account document (handles legacy seeded accounts accurately).
        var explicitRole = GetBsonString(doc, "Role", "role", "roleName", "RoleName");
        if (!string.IsNullOrWhiteSpace(explicitRole))
        {
            return RoleNames.Normalize(explicitRole);
        }

        if (labProfiles.ContainsKey(id))
            return RoleNames.Lab;

        if (doctors.ContainsKey(id))
            return RoleNames.Doctor;

        var roleId = GetBsonInt(doc, "RoleId", "roleId", "role_id");
        if (!roleId.HasValue)
            return null;

        if (roleId.Value == User.LegacyPatientRoleId || roleId.Value == (int)UserRole.Patient)
            return RoleNames.Patient;

        // Legacy self-registered patient documents had RoleId = 6 alongside patientCode/bloodType fields.
        if (roleId.Value == (int)UserRole.Manager)
        {
            var hasPatientSchema = doc.Contains("patientCode") || doc.Contains("bloodType") ||
                username.StartsWith("patient", StringComparison.OrdinalIgnoreCase);
            return hasPatientSchema ? RoleNames.Patient : RoleNames.Manager;
        }

        return RoleNames.FromRoleId(roleId.Value);
    }

    private static string BuildDefaultEmployeeCode(string canonicalRole, string id, string username)
    {
        var suffix = id.Length >= 3 ? id[^3..] : (!string.IsNullOrWhiteSpace(username) ? username : "001");
        return canonicalRole switch
        {
            RoleNames.Receptionist => $"NV-REC-{suffix}",
            RoleNames.Doctor => $"BS-{suffix}",
            RoleNames.Lab => $"KTV-{suffix}",
            RoleNames.Manager => $"QL-{suffix}",
            RoleNames.Admin => $"QT-{suffix}",
            _ => suffix
        };
    }

    private static string ResolveDefaultDepartment(string canonicalRole) => canonicalRole switch
    {
        RoleNames.Lab or RoleNames.Manager => "Khoa Xét Nghiệm",
        RoleNames.Doctor => "Khoa Khám Bệnh",
        RoleNames.Receptionist => "Khoa Khám Bệnh / Tiếp Đón",
        RoleNames.Admin => "Quản Trị Hệ Thống",
        _ => "Bệnh Viện BMS"
    };

    private static int GetRoleDisplayOrder(string canonicalRole) => canonicalRole switch
    {
        RoleNames.Lab => 1,
        RoleNames.Receptionist => 2,
        RoleNames.Manager => 3,
        RoleNames.Doctor => 4,
        RoleNames.Admin => 5,
        _ => 9
    };

    private static string ExtractBsonId(BsonDocument doc)
    {
        if (!doc.TryGetValue("_id", out var idVal) || idVal.IsBsonNull)
            return string.Empty;
        return idVal.IsObjectId ? idVal.AsObjectId.ToString() : idVal.ToString() ?? string.Empty;
    }

    private static string GetBsonString(BsonDocument doc, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (doc.TryGetValue(field, out var val) && !val.IsBsonNull && val.IsString && !string.IsNullOrWhiteSpace(val.AsString))
                return val.AsString.Trim();
        }
        return string.Empty;
    }

    private static int? GetBsonInt(BsonDocument doc, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (doc.TryGetValue(field, out var val) && !val.IsBsonNull)
            {
                if (val.IsInt32) return val.AsInt32;
                if (val.IsInt64) return (int)val.AsInt64;
                if (val.IsString && int.TryParse(val.AsString, out var parsed)) return parsed;
            }
        }
        return null;
    }

    private static double? GetBsonDouble(BsonDocument doc, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (doc.TryGetValue(field, out var val) && !val.IsBsonNull && val.IsNumeric)
                return val.ToDouble();
        }
        return null;
    }

    private async Task<Sample?> FindSampleAsync(string idOrBarcode, CancellationToken ct = default)
    {
        if (SamplesCol != null)
        {
            try
            {
                var fb = Builders<Sample>.Filter;
                // Exact Id match first (UUID / sample PK) — unambiguous.
                var byId = await SamplesCol.Find(fb.Eq(s => s.Id, idOrBarcode)).FirstOrDefaultAsync(ct);
                if (byId != null)
                    return byId;

                // Barcode/RFID can collide after reject+recollect. Prefer the active
                // lifecycle specimen so Lab check-in / result entry are not stuck on Rejected.
                var barcodeMatches = await SamplesCol
                    .Find(fb.Or(
                        fb.Eq(s => s.Barcode, idOrBarcode),
                        fb.Eq(s => s.RfidTag, idOrBarcode)))
                    .ToListAsync(ct);

                if (barcodeMatches.Count > 0)
                    return PreferActiveSample(barcodeMatches);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to query blood_samples in MongoDB for {Key}", idOrBarcode);
            }
        }

        var memMatches = _context.Samples
            .Where(x =>
                x.Id.Equals(idOrBarcode, StringComparison.OrdinalIgnoreCase) ||
                x.Barcode.Equals(idOrBarcode, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return PreferActiveSample(memMatches);
    }

    /// <summary>
    /// When multiple specimens share a barcode, pick Id-exact or non-terminal first.
    /// Terminal statuses (Rejected/Completed) lose to PendingCollection…Testing/RerunRequested.
    /// </summary>
    private static Sample? PreferActiveSample(IReadOnlyList<Sample> matches)
    {
        if (matches == null || matches.Count == 0)
            return null;
        if (matches.Count == 1)
            return matches[0];

        static int Rank(Sample s)
        {
            var status = (s.StatusString ?? s.Status.ToString() ?? string.Empty).Trim();
            return status switch
            {
                nameof(SampleStatus.PendingCollection) => 0,
                nameof(SampleStatus.Collected) => 1,
                nameof(SampleStatus.InTransit) => 2,
                nameof(SampleStatus.Received) => 3,
                nameof(SampleStatus.Processing) => 4,
                nameof(SampleStatus.Testing) => 5,
                nameof(SampleStatus.RerunRequested) => 6,
                nameof(SampleStatus.Completed) => 80,
                nameof(SampleStatus.Rejected) => 90,
                _ => 50
            };
        }

        return matches
            .OrderBy(Rank)
            .ThenByDescending(s => s.UpdatedAt == default ? s.CollectedAt : s.UpdatedAt)
            .First();
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

        // Only use in-memory store if MongoDB has no samples (dev fallback)
        if (byId.Count == 0)
        {
            foreach (var memSample in _context.Samples)
            {
                byId.TryAdd(memSample.Id, memSample);
            }
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
                var staffList = await GetStaffDirectoryAsync(ct);
                var uMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var st in staffList)
                {
                    if (!string.IsNullOrWhiteSpace(st.Id)) uMap.TryAdd(st.Id, st.FullName);
                    if (!string.IsNullOrWhiteSpace(st.EmployeeCode)) uMap.TryAdd(st.EmployeeCode, st.FullName);
                    if (!string.IsNullOrWhiteSpace(st.Username)) uMap.TryAdd(st.Username, st.FullName);
                }

                var unresolvedIds = collectorIds.Where(cid => !uMap.ContainsKey(cid)).ToList();
                if (unresolvedIds.Count > 0)
                {
                    var users = await AccountsCol
                        .Find(Builders<User>.Filter.In(u => u.Id, unresolvedIds))
                        .ToListAsync(ct);
                    foreach (var u in users)
                    {
                        uMap.TryAdd(u.Id, u.FullName);
                    }
                }

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

            // Enrich TestPackageName, PackageId, and exact PackageParameters from linked Appointment / ServicePackage / TestOrder.
            var samplesNeedingPkg = samples
                .Where(s => string.IsNullOrWhiteSpace(s.TestPackageName) || s.PackageParameters == null || s.PackageParameters.Count == 0)
                .ToList();
            if (samplesNeedingPkg.Count > 0)
            {
                var pkgDocByKey = new Dictionary<string, BsonDocument>(StringComparer.OrdinalIgnoreCase);
                var pkgNameByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var pkgCanonicalIdByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Load service_packages first so Admin-configured Tests and Names take priority, then test_packages.
                foreach (var pkgCol in new[] { ServicePackagesCol, TestPackagesCol })
                {
                    if (pkgCol == null) continue;
                    try
                    {
                        var pkgDocs = await pkgCol.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct);
                        foreach (var pd in pkgDocs)
                        {
                            var name = GetBsonString(pd, "package_name", "Name", "service_name", "name");
                            var pid = ExtractBsonId(pd);
                            var keys = new List<string>();
                            if (!string.IsNullOrWhiteSpace(pid)) keys.Add(pid);
                            foreach (var codeField in new[] { "Code", "package_code", "service_code", "code" })
                            {
                                var codeVal = GetBsonString(pd, codeField);
                                if (!string.IsNullOrWhiteSpace(codeVal)) keys.Add(codeVal);
                            }
                            if (!string.IsNullOrWhiteSpace(name)) keys.Add(name);

                            var hasTests = (pd.TryGetValue("Tests", out var t1) && t1.IsBsonArray && t1.AsBsonArray.Count > 0)
                                        || (pd.TryGetValue("tests", out var t2) && t2.IsBsonArray && t2.AsBsonArray.Count > 0);

                            foreach (var k in keys)
                            {
                                if (!string.IsNullOrWhiteSpace(name)) pkgNameByKey.TryAdd(k, name);
                                if (!string.IsNullOrWhiteSpace(pid)) pkgCanonicalIdByKey.TryAdd(k, pid);
                                if (!pkgDocByKey.TryGetValue(k, out var existingDoc))
                                {
                                    pkgDocByKey[k] = pd;
                                }
                                else if (hasTests)
                                {
                                    var existingHasTests = (existingDoc.TryGetValue("Tests", out var et1) && et1.IsBsonArray && et1.AsBsonArray.Count > 0)
                                                        || (existingDoc.TryGetValue("tests", out var et2) && et2.IsBsonArray && et2.AsBsonArray.Count > 0);
                                    if (!existingHasTests) pkgDocByKey[k] = pd;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Non-fatal
                    }
                }

                // Load master test_criteria (preferring canonical active documents with ranges/panic thresholds).
                var criteriaByCode = new Dictionary<string, SampleExpectedParameter>(StringComparer.OrdinalIgnoreCase);
                var criteriaCodeById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (TestCriteriaCol != null)
                {
                    try
                    {
                        var critDocs = await TestCriteriaCol.Find(
                            Builders<BsonDocument>.Filter.Ne("isDeleted", true) &
                            Builders<BsonDocument>.Filter.Ne("is_deleted", true)).ToListAsync(ct);

                        foreach (var cd in critDocs)
                        {
                            var cid = ExtractBsonId(cd);
                            var code = GetBsonString(cd, "criterionCode", "criterion_code", "code");
                            if (string.IsNullOrWhiteSpace(code)) continue;
                            if (!string.IsNullOrWhiteSpace(cid)) criteriaCodeById[cid] = code;

                            var isActive = !cd.TryGetValue("isActive", out var actVal) || !actVal.IsBoolean || actVal.AsBoolean;
                            if (!isActive) continue;

                            var isCanonical = cd.Contains("criterionCode") || cd.Contains("normalMin") || cd.Contains("normalMax");
                            var candidate = new SampleExpectedParameter
                            {
                                Code = code.ToUpperInvariant(),
                                Name = GetBsonString(cd, "criterionName", "criterion_name", "name"),
                                Unit = GetBsonString(cd, "unit"),
                                NormalMin = GetBsonDouble(cd, "normalMin", "normal_min"),
                                NormalMax = GetBsonDouble(cd, "normalMax", "normal_max"),
                                NormalText = string.IsNullOrWhiteSpace(GetBsonString(cd, "normalText", "normal_text", "reference_range"))
                                    ? null
                                    : GetBsonString(cd, "normalText", "normal_text", "reference_range"),
                                PanicLow = GetBsonDouble(cd, "panicLow", "panic_low"),
                                PanicHigh = GetBsonDouble(cd, "panicHigh", "panic_high"),
                                ValueType = string.IsNullOrWhiteSpace(GetBsonString(cd, "valueType", "value_type"))
                                    ? "Numeric"
                                    : GetBsonString(cd, "valueType", "value_type")
                            };
                            if (string.IsNullOrWhiteSpace(candidate.Name)) candidate.Name = candidate.Code;

                            if (!criteriaByCode.ContainsKey(code) || isCanonical)
                            {
                                criteriaByCode[code] = candidate;
                            }
                        }
                    }
                    catch
                    {
                        // Non-fatal
                    }
                }

                // Load test_package_criteria lookup (package_id -> list of criterion codes).
                var mappedCodesByPkgId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                if (TestPackageCriteriaCol != null)
                {
                    try
                    {
                        var maps = await TestPackageCriteriaCol.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct);
                        foreach (var m in maps)
                        {
                            var mapPkgId = GetBsonString(m, "package_id", "packageId");
                            var mapCritId = GetBsonString(m, "criteria_id", "criteriaId", "criterion_id");
                            if (string.IsNullOrWhiteSpace(mapPkgId) || string.IsNullOrWhiteSpace(mapCritId)) continue;
                            var resolvedCode = criteriaCodeById.TryGetValue(mapCritId, out var cCode) ? cCode : mapCritId;
                            if (!mappedCodesByPkgId.TryGetValue(mapPkgId, out var list))
                            {
                                list = [];
                                mappedCodesByPkgId[mapPkgId] = list;
                            }
                            if (!list.Contains(resolvedCode, StringComparer.OrdinalIgnoreCase))
                                list.Add(resolvedCode);
                        }
                    }
                    catch
                    {
                        // Non-fatal
                    }
                }

                // Map PatientId -> PatientCode so appointments stored under either key can be matched.
                var patientCodeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (PatientsCol != null)
                {
                    var pids = samplesNeedingPkg.Select(s => s.PatientId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                    if (pids.Count > 0)
                    {
                        try
                        {
                            var pts = await PatientsCol.Find(Builders<Patient>.Filter.In(p => p.Id, pids)).ToListAsync(ct);
                            foreach (var pt in pts)
                            {
                                if (!string.IsNullOrWhiteSpace(pt.PatientCode))
                                    patientCodeMap[pt.Id] = pt.PatientCode;
                            }
                        }
                        catch { }
                    }
                }

                foreach (var s in samplesNeedingPkg)
                {
                    string? resolvedPkgId = s.PackageId;
                    string? resolvedPkgName = string.IsNullOrWhiteSpace(s.TestPackageName) ? null : s.TestPackageName;
                    var rawParams = new List<(string Code, string Name)>();

                    if (AppointmentsCol != null)
                    {
                        try
                        {
                            var af = Builders<BsonDocument>.Filter;
                            BsonDocument? apptDoc = null;
                            if (!string.IsNullOrWhiteSpace(s.TestOrderId))
                            {
                                var apptFilters = new List<FilterDefinition<BsonDocument>>
                                {
                                    af.Eq("_id", s.TestOrderId),
                                    af.Eq("appointmentCode", s.TestOrderId),
                                    af.Eq("appointment_code", s.TestOrderId)
                                };
                                if (ObjectId.TryParse(s.TestOrderId, out var apptOid))
                                    apptFilters.Add(af.Eq("_id", apptOid));

                                apptDoc = await AppointmentsCol.Find(af.Or(apptFilters))
                                    .FirstOrDefaultAsync(ct);
                            }

                            if (apptDoc == null && !string.IsNullOrWhiteSpace(s.PatientId))
                            {
                                var pKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { s.PatientId };
                                if (patientCodeMap.TryGetValue(s.PatientId, out var pCode)) pKeys.Add(pCode);

                                apptDoc = await AppointmentsCol.Find(af.And(
                                        af.Or(af.In("patientId", pKeys), af.In("patient_id", pKeys), af.In("PatientId", pKeys)),
                                        af.Nin("status", new BsonValue[] { "Cancelled", "cancelled", 5 }),
                                        af.Nin("Status", new BsonValue[] { "Cancelled", "cancelled", 5 })))
                                    .Sort(Builders<BsonDocument>.Sort.Descending("createdAt").Descending("created_at").Descending("CreatedAt"))
                                    .FirstOrDefaultAsync(ct);
                            }

                            if (apptDoc != null)
                            {
                                var pkgId = GetBsonString(apptDoc, "packageId", "package_id", "PackageId");
                                if (!string.IsNullOrWhiteSpace(pkgId)) resolvedPkgId = pkgId;

                                var inlinePkgName = GetBsonString(apptDoc, "packageName", "package_name");
                                if (!string.IsNullOrWhiteSpace(inlinePkgName))
                                {
                                    resolvedPkgName ??= inlinePkgName;
                                }
                                else if (!string.IsNullOrWhiteSpace(pkgId))
                                {
                                    resolvedPkgName ??= pkgNameByKey.TryGetValue(pkgId, out var mappedName) ? mappedName : pkgId;
                                }
                            }
                        }
                        catch { }
                    }

                    // 1. Try package.Tests from service_packages
                    var lookupKeys = new[] { resolvedPkgId, s.PackageCode, resolvedPkgName }
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Select(k => k!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    BsonDocument? matchedPkgDoc = null;
                    foreach (var lk in lookupKeys)
                    {
                        if (pkgDocByKey.TryGetValue(lk, out matchedPkgDoc)) break;
                    }

                    if (matchedPkgDoc != null)
                    {
                        var testsArray = matchedPkgDoc.TryGetValue("Tests", out var tv1) && tv1.IsBsonArray ? tv1.AsBsonArray
                            : (matchedPkgDoc.TryGetValue("tests", out var tv2) && tv2.IsBsonArray ? tv2.AsBsonArray : null);
                        if (testsArray != null)
                        {
                            foreach (var tItem in testsArray.Where(x => x.IsBsonDocument).Select(x => x.AsBsonDocument))
                            {
                                var tc = GetBsonString(tItem, "Code", "code", "criterionCode");
                                var tn = GetBsonString(tItem, "Name", "name", "criterionName");
                                if (!string.IsNullOrWhiteSpace(tc))
                                    rawParams.Add((tc, tn));
                            }
                        }
                    }

                    // 2. Fallback to test_package_criteria mappings (e.g. PKG-CBC-01 -> CRT-RBC, CRT-HGB, ...)
                    if (rawParams.Count == 0)
                    {
                        var candidatePkgIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var lk in lookupKeys)
                        {
                            candidatePkgIds.Add(lk);
                            if (pkgCanonicalIdByKey.TryGetValue(lk, out var cid)) candidatePkgIds.Add(cid);
                        }
                        foreach (var cpid in candidatePkgIds)
                        {
                            if (mappedCodesByPkgId.TryGetValue(cpid, out var mappedCodes) && mappedCodes.Count > 0)
                            {
                                foreach (var mc in mappedCodes)
                                    rawParams.Add((mc, string.Empty));
                                break;
                            }
                        }
                    }

                    // 3. Fallback to UC32 test_orders items
                    if (TestOrdersCol != null && (string.IsNullOrWhiteSpace(resolvedPkgName) || rawParams.Count == 0))
                    {
                        try
                        {
                            var of = Builders<BsonDocument>.Filter;
                            BsonDocument? orderDoc = null;
                            if (!string.IsNullOrWhiteSpace(s.TestOrderId))
                            {
                                var orderFilters = new List<FilterDefinition<BsonDocument>>
                                {
                                    of.Eq("_id", s.TestOrderId),
                                    of.Eq("order_number", s.TestOrderId),
                                    of.Eq("OrderNumber", s.TestOrderId)
                                };
                                if (ObjectId.TryParse(s.TestOrderId, out var orderOid))
                                    orderFilters.Add(of.Eq("_id", orderOid));

                                orderDoc = await TestOrdersCol.Find(of.Or(orderFilters))
                                    .FirstOrDefaultAsync(ct);
                            }
                            if (orderDoc != null && orderDoc.TryGetValue("items", out var itemsVal) && itemsVal.IsBsonArray)
                            {
                                var names = new List<string>();
                                foreach (var itemDoc in itemsVal.AsBsonArray.Where(x => x.IsBsonDocument).Select(x => x.AsBsonDocument))
                                {
                                    var tName = GetBsonString(itemDoc, "test_name", "testName", "TestName");
                                    var tCode = GetBsonString(itemDoc, "test_code", "testCode", "TestCode");
                                    if (!string.IsNullOrWhiteSpace(tName)) names.Add(tName);
                                    if (rawParams.Count == 0 && !string.IsNullOrWhiteSpace(tCode))
                                        rawParams.Add((tCode, tName));
                                }
                                if (string.IsNullOrWhiteSpace(resolvedPkgName) && names.Count > 0)
                                    resolvedPkgName = string.Join(", ", names.Distinct());
                            }
                        }
                        catch { }
                    }

                    if (!string.IsNullOrWhiteSpace(resolvedPkgId)) s.PackageId = resolvedPkgId;
                    if (!string.IsNullOrWhiteSpace(resolvedPkgName)) s.TestPackageName = resolvedPkgName;

                    if (rawParams.Count > 0)
                    {
                        var enrichedParams = new List<SampleExpectedParameter>();
                        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (code, fallbackName) in rawParams)
                        {
                            var normCode = code.Trim().ToUpperInvariant();
                            if (string.IsNullOrWhiteSpace(normCode) || !seen.Add(normCode)) continue;

                            if (criteriaByCode.TryGetValue(normCode, out var master))
                            {
                                enrichedParams.Add(new SampleExpectedParameter
                                {
                                    Code = master.Code,
                                    Name = !string.IsNullOrWhiteSpace(master.Name) ? master.Name : (!string.IsNullOrWhiteSpace(fallbackName) ? fallbackName : master.Code),
                                    Unit = master.Unit,
                                    NormalMin = master.NormalMin,
                                    NormalMax = master.NormalMax,
                                    NormalText = master.NormalText,
                                    PanicLow = master.PanicLow,
                                    PanicHigh = master.PanicHigh,
                                    ValueType = master.ValueType
                                });
                            }
                            else
                            {
                                enrichedParams.Add(new SampleExpectedParameter
                                {
                                    Code = normCode,
                                    Name = !string.IsNullOrWhiteSpace(fallbackName) ? fallbackName : normCode,
                                    Unit = string.Empty,
                                    ValueType = "Numeric"
                                });
                            }
                        }
                        s.PackageParameters = enrichedParams;
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
            var key = userId.Trim();
            var fb = Builders<User>.Filter;
            var user = await AccountsCol.Find(fb.Or(
                fb.Eq(u => u.Id, key),
                fb.Eq(u => u.EmployeeCode, key),
                fb.Eq(u => u.Username, key))).FirstOrDefaultAsync(ct);

            if (user != null)
                return user.FullName;

            var staff = await GetStaffDirectoryAsync(ct);
            var matched = staff.FirstOrDefault(s =>
                string.Equals(s.Id, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.EmployeeCode, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Username, key, StringComparison.OrdinalIgnoreCase));
            return matched?.FullName;
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
        PackageId = s.PackageId,
        PackageCode = s.PackageCode,
        PackageParameters = (s.PackageParameters ?? []).Select(p => new SampleExpectedParameterDto
        {
            Code = p.Code,
            Name = p.Name,
            Unit = p.Unit,
            NormalMin = p.NormalMin,
            NormalMax = p.NormalMax,
            NormalText = p.NormalText,
            PanicLow = p.PanicLow,
            PanicHigh = p.PanicHigh,
            ValueType = p.ValueType
        }).ToList(),
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
        ReceivedBy = s.ReceivedBy,
        HandoverBy = s.HandoverBy,
        HandoverById = s.HandoverById,
        RejectionReason = s.RejectionReason,
        RerunReason = s.RerunReason,
        CollectedAt = s.CollectedAt,
        InTransitAt = s.InTransitAt,
        ReceivedAt = s.ReceivedAt,
        ProcessedAt = s.ProcessedAt,
        CompletedAt = s.CompletedAt,
        RejectedAt = s.RejectedAt,
        RejectedBy = s.RejectedBy,
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
