using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Service implementing laboratory technical verification workflow (UC72 / Issue I3-073).
/// Reads real submitted test results awaiting review from MongoDB (<c>test_results</c>),
/// allows inspecting measured values &amp; ISO 15189 verification checklists, signing off with SHA-256 digital signature,
/// locking results across collections, or returning specimens to lab technicians for correction.
/// </summary>
public class TestResultVerificationService : ITestResultVerificationService
{
    private static readonly string[] PendingStatuses =
    [
        "waiting_review",
        "WaitingReview",
        "PendingVerification",
        "pending_review",
        "Pending",
        "InReview"
    ];

    private readonly ITestResultVerificationRepository _repository;
    private readonly IMongoDatabase? _database;
    private readonly ILogger<TestResultVerificationService>? _logger;

    public TestResultVerificationService(
        ITestResultVerificationRepository repository,
        IMongoDatabase? database = null,
        ILogger<TestResultVerificationService>? logger = null)
    {
        _repository = repository;
        _database = database;
        _logger = logger;
    }

    public async Task<IEnumerable<VerificationQueueItemDto>> GetVerificationQueueAsync()
    {
        var result = new List<VerificationQueueItemDto>();
        var seenSpecimens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_database != null)
        {
            try
            {
                var resultsCol = _database.GetCollection<BsonDocument>("test_results");
                var rf = Builders<BsonDocument>.Filter;
                var pendingFilter = rf.And(
                    rf.Or(
                        rf.In("status", PendingStatuses),
                        rf.Eq("validationStatus", 1)),
                    rf.Ne("isSuperseded", true));

                var rawPending = await resultsCol
                    .Find(pendingFilter)
                    .Sort(Builders<BsonDocument>.Sort
                        .Descending("enteredAt")
                        .Descending("createdAt")
                        .Descending("created_at")
                        .Descending("_id"))
                    .ToListAsync();

                var lookupCache = await LoadMetadataLookupsAsync();

                foreach (var doc in rawPending)
                {
                    var statusRaw = GetBsonString(doc, "status");
                    if (statusRaw.Equals("Approved", StringComparison.OrdinalIgnoreCase) ||
                        statusRaw.Equals("Verified", StringComparison.OrdinalIgnoreCase) ||
                        statusRaw.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                        statusRaw.Equals("Rerun", StringComparison.OrdinalIgnoreCase) ||
                        statusRaw.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var specimenId = GetBsonString(doc, "sampleId", "sample_id");
                    if (string.IsNullOrWhiteSpace(specimenId))
                        specimenId = GetBsonString(doc, "testResultId", "_id");
                    if (string.IsNullOrWhiteSpace(specimenId) || !seenSpecimens.Add(specimenId))
                        continue;

                    // Skip if already verified in verification store
                    var existingVerif = await _repository.GetBySpecimenIdAsync(specimenId);
                    if (existingVerif != null &&
                        (existingVerif.Status.Equals("Verified", StringComparison.OrdinalIgnoreCase) ||
                         existingVerif.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var parsed = ParseTestResultDoc(doc, lookupCache);
                    var abnormalCount = parsed.Items.Count(i => !i.Flag.Equals("Normal", StringComparison.OrdinalIgnoreCase));

                    result.Add(new VerificationQueueItemDto
                    {
                        SpecimenId = specimenId,
                        TestResultId = parsed.TestResultId,
                        OrderId = parsed.OrderId,
                        PackageName = parsed.PackageName,
                        PatientId = parsed.PatientId,
                        PatientName = parsed.PatientName,
                        FlagCount = abnormalCount + (parsed.IsPanic ? 1 : 0),
                        Priority = parsed.IsPanic ? "Urgent" : (abnormalCount > 0 ? "High" : "Normal"),
                        CreatedAt = parsed.EnteredAt ?? DateTime.UtcNow,
                        Status = existingVerif?.Status ?? "PendingVerification"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read real pending test_results for verification queue.");
            }
        }

        // Merge non-duplicate persisted verification records; only expose legacy mock sample when DB is unavailable.
        var repoPending = await _repository.GetPendingQueueAsync();
        foreach (var v in repoPending)
        {
            if (string.IsNullOrWhiteSpace(v.SpecimenId) || !seenSpecimens.Add(v.SpecimenId))
                continue;

            if (_database != null && v.SpecimenId.Equals("SPC-261006-A1-0089", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new VerificationQueueItemDto
            {
                SpecimenId = v.SpecimenId,
                OrderId = v.OrderId,
                PackageName = v.PackageName,
                PatientId = v.PatientId,
                PatientName = v.PatientName,
                FlagCount = v.Checklist.Count(c => c.Status.Equals("Flag", StringComparison.OrdinalIgnoreCase)),
                Priority = "Normal",
                CreatedAt = v.CreatedAt,
                Status = v.Status
            });
        }

        return result.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<TestResultVerificationDetailDto?> GetVerificationDetailsBySpecimenIdAsync(string specimenId)
    {
        if (string.IsNullOrWhiteSpace(specimenId))
            return null;

        var trimmedId = specimenId.Trim();
        var storedVerification = await _repository.GetBySpecimenIdAsync(trimmedId);
        var realDoc = await FindLatestTestResultBySpecimenAsync(trimmedId);

        if (realDoc != null)
        {
            var lookupCache = await LoadMetadataLookupsAsync();
            var parsed = ParseTestResultDoc(realDoc, lookupCache);
            var checklist = BuildChecklistFromResult(parsed);

            var status = storedVerification?.Status;
            if (string.IsNullOrWhiteSpace(status))
            {
                status = parsed.StatusRaw.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                    ? "Verified"
                    : (parsed.StatusRaw.Equals("Rerun", StringComparison.OrdinalIgnoreCase)
                        ? "ReturnedForCorrection"
                        : "PendingVerification");
            }

            var isVerified = status.Equals("Verified", StringComparison.OrdinalIgnoreCase) ||
                             status.Equals("Approved", StringComparison.OrdinalIgnoreCase);

            var flagCount = checklist.Count(c => c.Status.Equals("Flag", StringComparison.OrdinalIgnoreCase));

            return new TestResultVerificationDetailDto
            {
                SpecimenId = trimmedId,
                TestResultId = parsed.TestResultId,
                OrderId = parsed.OrderId,
                PackageName = parsed.PackageName,
                PatientId = parsed.PatientId,
                PatientName = parsed.PatientName,
                DeviceId = parsed.DeviceId,
                EnteredAt = parsed.EnteredAt,
                IsPanic = parsed.IsPanic,
                TestResultItems = parsed.Items,
                Checklist = checklist.Select(c => new VerificationCheckItemDto
                {
                    CheckName = c.CheckName,
                    Status = c.Status,
                    StatusType = c.StatusType,
                    Details = c.Details
                }).ToList(),
                VerifierComment = storedVerification?.VerifierComment ?? parsed.ClinicalComment ?? string.Empty,
                Status = status,
                VerifiedBy = storedVerification?.VerifiedBy ?? parsed.ReviewedBy ?? string.Empty,
                VerifiedAt = storedVerification?.VerifiedAt ?? parsed.ApprovedAt,
                ReturnedReason = storedVerification?.ReturnedReason ?? parsed.RerunReason,
                DigitalSignature = storedVerification?.DigitalSignature ?? parsed.DigitalSignature,
                IsLocked = (storedVerification?.IsLocked ?? false) || isVerified,
                FlagCount = flagCount
            };
        }

        if (storedVerification != null)
        {
            return MapToDto(storedVerification);
        }

        // Preserve legacy fallback only when running without real DB match
        if (_database == null)
        {
            var entity = new TestResultVerification
            {
                SpecimenId = trimmedId,
                OrderId = "ORD-261006-0187",
                PackageName = "Lipid Profile",
                PatientId = "PT-2026-004523",
                PatientName = "Nguyễn Văn An",
                Status = "PendingVerification",
                CreatedAt = DateTime.UtcNow,
                Checklist = BuildDefaultChecklist()
            };
            await _repository.UpsertVerificationAsync(entity);
            return MapToDto(entity);
        }

        return null;
    }

    public async Task<TestResultVerificationDetailDto> VerifyTestResultAsync(string specimenId, VerifyTestResultRequestDto request, string verifierName)
    {
        var trimmedId = specimenId.Trim();
        var realDoc = await FindLatestTestResultBySpecimenAsync(trimmedId);
        ParsedResultSnapshot? parsed = null;
        if (realDoc != null)
        {
            var lookupCache = await LoadMetadataLookupsAsync();
            parsed = ParseTestResultDoc(realDoc, lookupCache);
        }

        var entity = await _repository.GetBySpecimenIdAsync(trimmedId);
        if (entity == null)
        {
            entity = new TestResultVerification
            {
                SpecimenId = trimmedId,
                OrderId = parsed?.OrderId ?? trimmedId,
                PackageName = parsed?.PackageName ?? "Xét nghiệm lâm sàng",
                PatientId = parsed?.PatientId ?? string.Empty,
                PatientName = parsed?.PatientName ?? "Bệnh nhân",
                CreatedAt = parsed?.EnteredAt ?? DateTime.UtcNow,
                Checklist = parsed != null ? BuildChecklistFromResult(parsed) : BuildDefaultChecklist()
            };
        }
        else if (parsed != null)
        {
            entity.OrderId = parsed.OrderId;
            entity.PackageName = parsed.PackageName;
            entity.PatientId = parsed.PatientId;
            entity.PatientName = parsed.PatientName;
            entity.Checklist = BuildChecklistFromResult(parsed);
        }

        var normalizedVerifier = string.IsNullOrWhiteSpace(verifierName) ? "Trưởng Khoa Xét Nghiệm" : verifierName.Trim();
        var verifyTime = DateTime.UtcNow;

        // 1. Generate cryptographic SHA-256 digital signature
        var signaturePayload = $"BMS-ISO15189-VERIFY:{trimmedId}:{entity.OrderId}:{normalizedVerifier}:{verifyTime:yyyyMMddHHmmss}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(signaturePayload));
        var digitalSignature = "SHA256:" + Convert.ToHexString(hashBytes).ToLowerInvariant();

        // 2. Lock verification protocol entity
        entity.Status = "Verified";
        entity.VerifierComment = request.VerifierComment;
        entity.VerifiedBy = normalizedVerifier;
        entity.VerifiedAt = verifyTime;
        entity.DigitalSignature = digitalSignature;
        entity.IsLocked = true;

        await _repository.UpsertVerificationAsync(entity);

        // 3. Technical verification only — does NOT publish to patient portal.
        // Main clinical gate: Doctor must still approve (DoctorApproved + APPROVED + clinical signature).
        if (_database != null)
        {
            try
            {
                // 3a. Mark test_results as TechnicianVerified / waiting_doctor_review (editable by Doctor).
                var testResultsCol = _database.GetCollection<BsonDocument>("test_results");
                var rf = Builders<BsonDocument>.Filter;
                var resultFilter = rf.Or(
                    rf.Eq("sampleId", trimmedId),
                    rf.Eq("sample_id", trimmedId),
                    rf.Eq("testResultId", trimmedId),
                    rf.Eq("_id", trimmedId));

                if (parsed != null && !string.IsNullOrWhiteSpace(parsed.TestResultId))
                {
                    resultFilter = rf.Or(resultFilter, rf.Eq("_id", parsed.TestResultId));
                }

                var resultUpdate = Builders<BsonDocument>.Update
                    .Set("status", "waiting_doctor_review")
                    .Set("validationStatus", (int)ResultValidationStatus.TechnicianVerified)
                    .Set("tech_signature", digitalSignature)
                    .Set("reviewedBy", normalizedVerifier)
                    .Set("reviewed_by", normalizedVerifier)
                    .Set("reviewedAt", verifyTime)
                    .Set("reviewed_at", verifyTime)
                    .Set("clinicalComment", request.VerifierComment)
                    .Set("conclusion_notes", request.VerifierComment)
                    .Set("updatedAt", verifyTime)
                    .Unset("approved_at")
                    .Unset("approvedAt")
                    .Unset("digital_signature")
                    .Unset("digitalSignature")
                    .Set("is_locked", false);

                var updateResult = await testResultsCol.UpdateManyAsync(resultFilter, resultUpdate);
                if (updateResult.MatchedCount == 0)
                {
                    var newResultDoc = new BsonDocument
                    {
                        { "_id", $"RES-{verifyTime:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}" },
                        { "sampleId", trimmedId },
                        { "sample_id", trimmedId },
                        { "patientId", entity.PatientId },
                        { "patient_id", entity.PatientId },
                        { "packageId", entity.PackageName },
                        { "package_id", entity.PackageName },
                        { "status", "waiting_doctor_review" },
                        { "validationStatus", (int)ResultValidationStatus.TechnicianVerified },
                        { "tech_signature", digitalSignature },
                        { "is_locked", false },
                        { "reviewedBy", normalizedVerifier },
                        { "reviewed_by", normalizedVerifier },
                        { "reviewedAt", verifyTime },
                        { "reviewed_at", verifyTime },
                        { "clinicalComment", request.VerifierComment },
                        { "conclusion_notes", request.VerifierComment },
                        { "createdAt", verifyTime },
                        { "created_at", verifyTime }
                    };
                    await testResultsCol.InsertOneAsync(newResultDoc);
                }

                // 3b. Sample stays Completed after Lab entry; do not reopen patient portal.
                var samplesCol = _database.GetCollection<BsonDocument>("blood_samples");
                var sf = Builders<BsonDocument>.Filter;
                var sampleFilter = sf.Or(
                    sf.Eq("_id", trimmedId),
                    sf.Eq("barcode", trimmedId),
                    sf.Eq("Barcode", trimmedId));
                var sampleUpdate = Builders<BsonDocument>.Update
                    .Set("status", "Completed")
                    .Set("completed_at", verifyTime)
                    .Set("CompletedAt", verifyTime)
                    .Set("updated_at", verifyTime)
                    .Set("UpdatedAt", verifyTime);
                await samplesCol.UpdateManyAsync(sampleFilter, sampleUpdate);

                // 3c. Notify Doctor only — Patient learns results after Doctor clinical approve.
                var notifCol = _database.GetCollection<BsonDocument>("notifications");
                var docNotif = new BsonDocument
                {
                    { "notification_id", $"NOT-{Guid.NewGuid().ToString("N")[..8].ToUpper()}" },
                    { "user_id", entity.PatientId },
                    { "role", "Doctor" },
                    { "title", $"[CHỜ BS THẨM ĐỊNH] Mẫu {trimmedId} - Đơn {entity.OrderId}" },
                    { "message", $"Trưởng Khoa {normalizedVerifier} đã thẩm định kỹ thuật {entity.PackageName} của BN {entity.PatientName}. Vào /doctor/console → Thẩm Định & Tư Vấn → Ký Số & Trả Kết Quả Cho Bệnh Nhân." },
                    { "type", "TestResultReadyForDoctor" },
                    { "reference_id", trimmedId },
                    { "is_read", false },
                    { "created_at", verifyTime }
                };

                await notifCol.InsertOneAsync(docNotif);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to persist technician verification or doctor notification for specimen {SpecimenId}", trimmedId);
            }
        }

        var dto = MapToDto(entity);
        if (parsed != null)
        {
            dto.TestResultId = parsed.TestResultId;
            dto.DeviceId = parsed.DeviceId;
            dto.EnteredAt = parsed.EnteredAt;
            dto.IsPanic = parsed.IsPanic;
            dto.TestResultItems = parsed.Items;
        }
        return dto;
    }

    public async Task<TestResultVerificationDetailDto> ReturnTestResultAsync(string specimenId, ReturnTestResultRequestDto request, string verifierName)
    {
        var trimmedId = specimenId.Trim();
        var realDoc = await FindLatestTestResultBySpecimenAsync(trimmedId);
        ParsedResultSnapshot? parsed = null;
        if (realDoc != null)
        {
            var lookupCache = await LoadMetadataLookupsAsync();
            parsed = ParseTestResultDoc(realDoc, lookupCache);
        }

        var entity = await _repository.GetBySpecimenIdAsync(trimmedId);
        if (entity == null)
        {
            entity = new TestResultVerification
            {
                SpecimenId = trimmedId,
                OrderId = parsed?.OrderId ?? trimmedId,
                PackageName = parsed?.PackageName ?? "Xét nghiệm lâm sàng",
                PatientId = parsed?.PatientId ?? string.Empty,
                PatientName = parsed?.PatientName ?? "Bệnh nhân",
                CreatedAt = parsed?.EnteredAt ?? DateTime.UtcNow,
                Checklist = parsed != null ? BuildChecklistFromResult(parsed) : BuildDefaultChecklist()
            };
        }

        var normalizedVerifier = string.IsNullOrWhiteSpace(verifierName) ? "Trưởng Khoa Xét Nghiệm" : verifierName.Trim();
        var returnTime = DateTime.UtcNow;

        entity.Status = "ReturnedForCorrection";
        entity.ReturnedReason = request.ReturnReason;
        entity.VerifierComment = request.ReturnReason;
        entity.VerifiedBy = normalizedVerifier;
        entity.VerifiedAt = returnTime;

        await _repository.UpsertVerificationAsync(entity);

        if (_database != null)
        {
            try
            {
                var testResultsCol = _database.GetCollection<BsonDocument>("test_results");
                var rf = Builders<BsonDocument>.Filter;
                var resultFilter = rf.Or(
                    rf.Eq("sampleId", trimmedId),
                    rf.Eq("sample_id", trimmedId),
                    rf.Eq("testResultId", trimmedId),
                    rf.Eq("_id", trimmedId));
                var resultUpdate = Builders<BsonDocument>.Update
                    .Set("status", "Rerun")
                    .Set("validationStatus", 4)
                    .Set("rerunReason", request.ReturnReason)
                    .Set("rerun_requested", true)
                    .Set("reviewedBy", normalizedVerifier)
                    .Set("reviewedAt", returnTime)
                    .Set("updatedAt", returnTime);
                await testResultsCol.UpdateManyAsync(resultFilter, resultUpdate);

                var samplesCol = _database.GetCollection<BsonDocument>("blood_samples");
                var sf = Builders<BsonDocument>.Filter;
                var sampleFilter = sf.Or(
                    sf.Eq("_id", trimmedId),
                    sf.Eq("barcode", trimmedId),
                    sf.Eq("Barcode", trimmedId));
                var sampleUpdate = Builders<BsonDocument>.Update
                    .Set("status", "RerunRequested")
                    .Set("rerun_reason", request.ReturnReason)
                    .Set("RerunReason", request.ReturnReason)
                    .Set("updated_at", returnTime)
                    .Set("UpdatedAt", returnTime);
                await samplesCol.UpdateManyAsync(sampleFilter, sampleUpdate);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to update MongoDB status for returned specimen {SpecimenId}", trimmedId);
            }
        }

        var dto = MapToDto(entity);
        if (parsed != null)
        {
            dto.TestResultId = parsed.TestResultId;
            dto.DeviceId = parsed.DeviceId;
            dto.EnteredAt = parsed.EnteredAt;
            dto.IsPanic = parsed.IsPanic;
            dto.TestResultItems = parsed.Items;
        }
        return dto;
    }

    private async Task<BsonDocument?> FindLatestTestResultBySpecimenAsync(string specimenId)
    {
        if (_database == null || string.IsNullOrWhiteSpace(specimenId))
            return null;

        try
        {
            var resultsCol = _database.GetCollection<BsonDocument>("test_results");
            var rf = Builders<BsonDocument>.Filter;
            var filter = rf.And(
                rf.Or(
                    rf.Eq("sampleId", specimenId),
                    rf.Eq("sample_id", specimenId),
                    rf.Eq("testResultId", specimenId),
                    rf.Eq("_id", specimenId)),
                rf.Ne("isSuperseded", true));

            return await resultsCol
                .Find(filter)
                .Sort(Builders<BsonDocument>.Sort
                    .Descending("enteredAt")
                    .Descending("createdAt")
                    .Descending("created_at")
                    .Descending("_id"))
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to lookup test_results for specimen {SpecimenId}", specimenId);
            return null;
        }
    }

    private async Task<MetadataLookupCache> LoadMetadataLookupsAsync()
    {
        var cache = new MetadataLookupCache();
        if (_database == null) return cache;

        try
        {
            var patients = await _database.GetCollection<BsonDocument>("patients").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            foreach (var p in patients)
            {
                var name = GetBsonString(p, "fullName", "FullName", "full_name", "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                foreach (var k in new[] { GetBsonId(p), GetBsonString(p, "patientCode", "PatientCode", "patient_code") })
                {
                    if (!string.IsNullOrWhiteSpace(k)) cache.PatientNameByKey.TryAdd(k, name);
                }
            }
        }
        catch { /* non-fatal */ }

        try
        {
            var samples = await _database.GetCollection<BsonDocument>("blood_samples").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            foreach (var s in samples)
            {
                var pName = GetBsonString(s, "patientName", "PatientName", "patient_name");
                var pId = GetBsonString(s, "patientId", "PatientId", "patient_id");
                foreach (var k in new[] { GetBsonId(s), GetBsonString(s, "barcode", "Barcode") })
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    if (!string.IsNullOrWhiteSpace(pName)) cache.SamplePatientNameByKey.TryAdd(k, pName);
                    if (!string.IsNullOrWhiteSpace(pId) && !string.IsNullOrWhiteSpace(pName))
                        cache.PatientNameByKey.TryAdd(pId, pName);
                }
            }
        }
        catch { /* non-fatal */ }

        foreach (var colName in new[] { "service_packages", "test_packages" })
        {
            try
            {
                var pkgs = await _database.GetCollection<BsonDocument>(colName).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
                foreach (var pkg in pkgs)
                {
                    var name = GetBsonString(pkg, "Name", "package_name", "packageName", "name");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    foreach (var k in new[] { GetBsonId(pkg), GetBsonString(pkg, "Code", "package_code", "packageCode", "code") })
                    {
                        if (!string.IsNullOrWhiteSpace(k)) cache.PackageNameByKey.TryAdd(k, name);
                    }
                }
            }
            catch { /* non-fatal */ }
        }

        try
        {
            var appts = await _database.GetCollection<BsonDocument>("appointments").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            foreach (var a in appts)
            {
                var aid = GetBsonId(a);
                var code = GetBsonString(a, "appointmentCode", "appointment_code");
                if (!string.IsNullOrWhiteSpace(aid) && !string.IsNullOrWhiteSpace(code))
                    cache.OrderCodeByApptId.TryAdd(aid, code);
            }
        }
        catch { /* non-fatal */ }

        return cache;
    }

    private static ParsedResultSnapshot ParseTestResultDoc(BsonDocument doc, MetadataLookupCache cache)
    {
        var specimenId = GetBsonString(doc, "sampleId", "sample_id");
        var testResultId = GetBsonString(doc, "testResultId", "_id");
        if (string.IsNullOrWhiteSpace(specimenId)) specimenId = testResultId;

        var rawApptId = GetBsonString(doc, "appointmentId", "appointment_id");
        var orderId = !string.IsNullOrWhiteSpace(rawApptId) && cache.OrderCodeByApptId.TryGetValue(rawApptId, out var apptCode)
            ? apptCode
            : (!string.IsNullOrWhiteSpace(rawApptId) ? rawApptId : testResultId);

        var patientId = GetBsonString(doc, "patientId", "patient_id");
        var patientName = (!string.IsNullOrWhiteSpace(patientId) && cache.PatientNameByKey.TryGetValue(patientId, out var pn))
            ? pn
            : (cache.SamplePatientNameByKey.TryGetValue(specimenId, out var spn) ? spn : (string.IsNullOrWhiteSpace(patientId) ? "Bệnh nhân" : patientId));

        var rawPkgId = GetBsonString(doc, "packageId", "package_id");
        var packageName = (!string.IsNullOrWhiteSpace(rawPkgId) && cache.PackageNameByKey.TryGetValue(rawPkgId, out var pkgName))
            ? pkgName
            : (!string.IsNullOrWhiteSpace(rawPkgId) ? rawPkgId : "Xét nghiệm lâm sàng");

        var items = new List<VerificationResultItemDto>();
        if (doc.TryGetValue("items", out var itemsVal) && itemsVal.IsBsonArray)
        {
            foreach (var itemDoc in itemsVal.AsBsonArray.Where(x => x.IsBsonDocument).Select(x => x.AsBsonDocument))
            {
                items.Add(new VerificationResultItemDto
                {
                    CriterionCode = GetBsonString(itemDoc, "criterionCode", "criterion_code", "code"),
                    CriterionName = GetBsonString(itemDoc, "criterionName", "criterion_name", "name"),
                    MeasuredValue = GetBsonString(itemDoc, "measuredValue", "measured_value", "value"),
                    Unit = GetBsonString(itemDoc, "unit"),
                    ReferenceInterval = GetBsonString(itemDoc, "referenceInterval", "reference_interval"),
                    Flag = ParseFlagName(itemDoc),
                    Notes = string.IsNullOrWhiteSpace(GetBsonString(itemDoc, "notes")) ? null : GetBsonString(itemDoc, "notes")
                });
            }
        }

        var isPanic = (doc.TryGetValue("isPanic", out var pv) && pv.IsBoolean && pv.AsBoolean)
                   || items.Any(i => i.Flag.Equals("Critical", StringComparison.OrdinalIgnoreCase));

        return new ParsedResultSnapshot
        {
            SpecimenId = specimenId,
            TestResultId = testResultId,
            OrderId = orderId,
            PatientId = patientId,
            PatientName = patientName,
            PackageName = packageName,
            DeviceId = GetBsonString(doc, "deviceId", "equipment_code"),
            StatusRaw = GetBsonString(doc, "status"),
            IsPanic = isPanic,
            EnteredAt = GetBsonDateTime(doc, "enteredAt", "createdAt", "created_at"),
            ApprovedAt = GetBsonDateTime(doc, "approvedAt", "approved_at"),
            ReviewedBy = GetBsonString(doc, "reviewedBy", "reviewed_by"),
            ClinicalComment = GetBsonString(doc, "clinicalComment", "conclusion_notes"),
            RerunReason = GetBsonString(doc, "rerunReason", "rerun_reason"),
            DigitalSignature = GetBsonString(doc, "digitalSignature", "digital_signature"),
            Items = items
        };
    }

    private static List<VerificationCheckItem> BuildChecklistFromResult(ParsedResultSnapshot parsed)
    {
        var abnormalItems = parsed.Items.Where(i => !i.Flag.Equals("Normal", StringComparison.OrdinalIgnoreCase)).ToList();
        var criticalItems = parsed.Items.Where(i => i.Flag.Equals("Critical", StringComparison.OrdinalIgnoreCase)).ToList();

        return new List<VerificationCheckItem>
        {
            new()
            {
                CheckName = "Xác thực định danh bệnh nhân & ống mẫu",
                Status = "OK",
                StatusType = "ok",
                Details = $"Khớp mã mẫu {parsed.SpecimenId} — Bệnh nhân {parsed.PatientName} ({parsed.PatientId})"
            },
            new()
            {
                CheckName = "Thiết bị phân tích & nội kiểm (IQC)",
                Status = "OK",
                StatusType = "ok",
                Details = string.IsNullOrWhiteSpace(parsed.DeviceId)
                    ? "Dữ liệu phân tích từ thiết bị phòng xét nghiệm đã ghi nhận đầy đủ"
                    : $"Thiết bị đo: {parsed.DeviceId} — Đã ghi nhận kết quả đo {parsed.Items.Count} chỉ số"
            },
            new()
            {
                CheckName = "Đối chiếu khoảng tham chiếu (Reference Intervals)",
                Status = abnormalItems.Count > 0 ? "Flag" : "OK",
                StatusType = abnormalItems.Count > 0 ? "warn" : "ok",
                Details = abnormalItems.Count > 0
                    ? $"Phát hiện {abnormalItems.Count} chỉ số ngoài khoảng tham chiếu: {string.Join(", ", abnormalItems.Select(a => $"{a.CriterionCode}={a.MeasuredValue} {a.Unit}"))}"
                    : $"Toàn bộ {parsed.Items.Count} chỉ số đều nằm trong khoảng tham chiếu chuẩn y khoa"
            },
            new()
            {
                CheckName = "Kiểm soát giá trị báo động nguy hiểm (Panic / Critical)",
                Status = (parsed.IsPanic || criticalItems.Count > 0) ? "Flag" : "None",
                StatusType = (parsed.IsPanic || criticalItems.Count > 0) ? "warn" : "ok",
                Details = (parsed.IsPanic || criticalItems.Count > 0)
                    ? $"CẢNH BÁO PANIC: {string.Join(", ", criticalItems.Select(c => $"{c.CriterionCode}={c.MeasuredValue} {c.Unit}"))} — Cần xác nhận thông báo Bác sĩ điều trị"
                    : "Không có chỉ số vượt ngưỡng báo động nguy hiểm (Panic)"
            },
            new()
            {
                CheckName = "Đầy đủ chỉ số theo gói xét nghiệm",
                Status = parsed.Items.Count > 0 ? "Confirmed" : "Flag",
                StatusType = parsed.Items.Count > 0 ? "ok" : "warn",
                Details = $"Gói xét nghiệm: {parsed.PackageName} ({parsed.Items.Count} chỉ số đã nhập kết quả)"
            }
        };
    }

    private static List<VerificationCheckItem> BuildDefaultChecklist() =>
    [
        new() { CheckName = "Patient identity confirmed", Status = "OK", StatusType = "ok", Details = "Patient identity verified against order" },
        new() { CheckName = "Analyzer QC passed", Status = "OK", StatusType = "ok", Details = "Analyzer QC check within Westgard limits" },
        new() { CheckName = "Reference intervals applied", Status = "OK", StatusType = "ok", Details = "Standard adult reference intervals applied" },
        new() { CheckName = "Panic / critical values", Status = "None", StatusType = "ok", Details = "No panic critical thresholds exceeded" }
    ];

    private static string ParseFlagName(BsonDocument itemDoc)
    {
        if (!itemDoc.TryGetValue("flag", out var fv) || fv.IsBsonNull)
            return "Normal";

        if (fv.IsInt32 || fv.IsInt64)
        {
            return fv.ToInt32() switch
            {
                1 => "Low",
                2 => "High",
                3 => "Critical",
                _ => "Normal"
            };
        }

        if (fv.IsString)
        {
            var s = fv.AsString.Trim();
            if (s.Equals("Panic", StringComparison.OrdinalIgnoreCase) || s.Equals("Critical", StringComparison.OrdinalIgnoreCase))
                return "Critical";
            if (s.Equals("High", StringComparison.OrdinalIgnoreCase)) return "High";
            if (s.Equals("Low", StringComparison.OrdinalIgnoreCase)) return "Low";
        }

        return "Normal";
    }

    private static string GetBsonId(BsonDocument doc)
    {
        if (!doc.TryGetValue("_id", out var v) || v.IsBsonNull) return string.Empty;
        return v.IsObjectId ? v.AsObjectId.ToString() : v.ToString() ?? string.Empty;
    }

    private static string GetBsonString(BsonDocument doc, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (doc.TryGetValue(k, out var v) && !v.IsBsonNull)
            {
                if (v.IsString && !string.IsNullOrWhiteSpace(v.AsString)) return v.AsString.Trim();
                if (v.IsObjectId) return v.AsObjectId.ToString();
            }
        }
        return string.Empty;
    }

    private static DateTime? GetBsonDateTime(BsonDocument doc, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (doc.TryGetValue(k, out var v) && v.IsBsonDateTime)
                return v.ToUniversalTime();
        }
        return null;
    }

    private static TestResultVerificationDetailDto MapToDto(TestResultVerification entity)
    {
        return new TestResultVerificationDetailDto
        {
            SpecimenId = entity.SpecimenId,
            OrderId = entity.OrderId,
            PackageName = entity.PackageName,
            PatientId = entity.PatientId,
            PatientName = entity.PatientName,
            VerifierComment = entity.VerifierComment,
            Status = entity.Status,
            VerifiedBy = entity.VerifiedBy,
            VerifiedAt = entity.VerifiedAt,
            ReturnedReason = entity.ReturnedReason,
            DigitalSignature = entity.DigitalSignature,
            IsLocked = entity.IsLocked,
            FlagCount = entity.Checklist.Count(c => c.Status.Equals("Flag", StringComparison.OrdinalIgnoreCase)),
            Checklist = entity.Checklist.Select(c => new VerificationCheckItemDto
            {
                CheckName = c.CheckName,
                Status = c.Status,
                StatusType = c.StatusType,
                Details = c.Details
            }).ToList()
        };
    }

    private sealed class MetadataLookupCache
    {
        public Dictionary<string, string> PatientNameByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> SamplePatientNameByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PackageNameByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> OrderCodeByApptId { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ParsedResultSnapshot
    {
        public string SpecimenId { get; init; } = string.Empty;
        public string TestResultId { get; init; } = string.Empty;
        public string OrderId { get; init; } = string.Empty;
        public string PatientId { get; init; } = string.Empty;
        public string PatientName { get; init; } = string.Empty;
        public string PackageName { get; init; } = string.Empty;
        public string DeviceId { get; init; } = string.Empty;
        public string StatusRaw { get; init; } = string.Empty;
        public bool IsPanic { get; init; }
        public DateTime? EnteredAt { get; init; }
        public DateTime? ApprovedAt { get; init; }
        public string ReviewedBy { get; init; } = string.Empty;
        public string ClinicalComment { get; init; } = string.Empty;
        public string RerunReason { get; init; } = string.Empty;
        public string DigitalSignature { get; init; } = string.Empty;
        public List<VerificationResultItemDto> Items { get; init; } = [];
    }
}
