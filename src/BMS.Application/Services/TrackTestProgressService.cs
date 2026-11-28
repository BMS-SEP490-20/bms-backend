using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Production service implementing UC48 Track Test Progress.
/// Conforms to ISO 15189 laboratory workflow, HIPAA privacy guidelines, and BR-PAT-02.
/// </summary>
public class TrackTestProgressService : ITrackTestProgressService
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<TestOrder> _ordersCollection;
    private readonly IMongoCollection<Sample> _samplesCollection;
    private readonly IMongoCollection<TestResult> _resultsCollection;

    public TrackTestProgressService(IMongoDatabase database)
    {
        _database = database;
        _ordersCollection = database.GetCollection<TestOrder>("test_orders");
        _samplesCollection = database.GetCollection<Sample>("blood_samples");
        _resultsCollection = database.GetCollection<TestResult>("test_results");
    }

    public async Task<TrackProgressResponse?> GetTrackingAsync(string identifier, bool isPublicQuery = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var cleanId = identifier.Trim();

        var order = await _ordersCollection.Find(x => x.OrderNumber == cleanId || x.Id == cleanId).FirstOrDefaultAsync(ct);

        Sample? sample = null;
        if (order != null)
        {
            sample = await _samplesCollection.Find(x => x.TestOrderId == order.Id || x.Barcode == cleanId).FirstOrDefaultAsync(ct);
        }
        else
        {
            sample = await _samplesCollection.Find(x => x.Barcode == cleanId || x.Id == cleanId).FirstOrDefaultAsync(ct);
            if (sample != null && !string.IsNullOrWhiteSpace(sample.TestOrderId))
            {
                order = await _ordersCollection.Find(x => x.Id == sample.TestOrderId || x.OrderNumber == sample.TestOrderId).FirstOrDefaultAsync(ct);
            }
        }

        if (order == null && sample == null)
            return null;

        var result = await FindBestTestResultForSampleAsync(sample, ct);
        return await BuildEnrichedTrackingResponseAsync(order, sample, result, isPublicQuery, ct);
    }

    public async Task<List<TrackProgressResponse>> GetPatientActiveTrackingsAsync(string patientId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(patientId))
            return new List<TrackProgressResponse>();

        var patientKeys = await ResolvePatientIdentifiersAsync(patientId.Trim(), ct);
        var list = new List<TrackProgressResponse>();
        var seenSampleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenOrderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var samples = await _samplesCollection
            .Find(Builders<Sample>.Filter.In(x => x.PatientId, patientKeys))
            .SortByDescending(x => x.CreatedAt)
            .Limit(30)
            .ToListAsync(ct);

        foreach (var sample in samples)
        {
            if (!seenSampleIds.Add(sample.Id)) continue;

            TestOrder? order = null;
            if (!string.IsNullOrWhiteSpace(sample.TestOrderId))
            {
                order = await _ordersCollection
                    .Find(x => x.Id == sample.TestOrderId || x.OrderNumber == sample.TestOrderId)
                    .FirstOrDefaultAsync(ct);
                if (order != null) seenOrderIds.Add(order.Id);
            }

            var result = await FindBestTestResultForSampleAsync(sample, ct);
            list.Add(await BuildEnrichedTrackingResponseAsync(order, sample, result, isPublicQuery: false, ct));
        }

        var orders = await _ordersCollection
            .Find(Builders<TestOrder>.Filter.In(x => x.PatientId, patientKeys))
            .SortByDescending(x => x.CreatedAt)
            .Limit(20)
            .ToListAsync(ct);

        foreach (var order in orders)
        {
            if (!seenOrderIds.Add(order.Id)) continue;
            var sample = await _samplesCollection.Find(x => x.TestOrderId == order.Id).FirstOrDefaultAsync(ct);
            if (sample != null && !seenSampleIds.Add(sample.Id)) continue;

            var result = await FindBestTestResultForSampleAsync(sample, ct);
            list.Add(await BuildEnrichedTrackingResponseAsync(order, sample, result, isPublicQuery: false, ct));
        }

        return list.OrderByDescending(x => x.OrderedAt).ToList();
    }

    public async Task<List<TrackProgressResponse>> SearchTrackingsAsync(
        string? query, string? status, DateTime? fromDate, DateTime? toDate, int limit = 50, CancellationToken ct = default)
    {
        var filterBuilder = Builders<TestOrder>.Filter;
        var filter = filterBuilder.Empty;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            filter &= filterBuilder.Or(
                filterBuilder.Regex(x => x.OrderNumber, new BsonRegularExpression(q, "i")),
                filterBuilder.Regex(x => x.PatientName, new BsonRegularExpression(q, "i")),
                filterBuilder.Regex(x => x.PatientId, new BsonRegularExpression(q, "i"))
            );
        }

        if (fromDate.HasValue)
        {
            filter &= filterBuilder.Gte(x => x.CreatedAt, fromDate.Value.Date);
        }

        if (toDate.HasValue)
        {
            filter &= filterBuilder.Lte(x => x.CreatedAt, toDate.Value.Date.AddDays(1).AddTicks(-1));
        }

        var orders = await _ordersCollection
            .Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Limit(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

        var list = new List<TrackProgressResponse>();
        foreach (var order in orders)
        {
            var sample = await _samplesCollection.Find(x => x.TestOrderId == order.Id).FirstOrDefaultAsync(ct);
            var result = await FindBestTestResultForSampleAsync(sample, ct);

            var item = await BuildEnrichedTrackingResponseAsync(order, sample, result, isPublicQuery: false, ct);
            if (!string.IsNullOrWhiteSpace(status) && !item.OverallStatus.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            list.Add(item);
        }

        return list;
    }

    private async Task<List<string>> ResolvePatientIdentifiersAsync(string inputId, CancellationToken ct)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { inputId };
        try
        {
            var patientsCol = _database.GetCollection<BsonDocument>("patients");
            var pf = Builders<BsonDocument>.Filter;
            var idValues = new List<BsonValue> { inputId };
            if (ObjectId.TryParse(inputId, out var oid)) idValues.Add(oid);

            var matches = await patientsCol.Find(pf.Or(
                pf.In("_id", idValues),
                pf.In("AccountId", idValues),
                pf.In("accountId", idValues),
                pf.Eq("patient_code", inputId),
                pf.Eq("patientCode", inputId))).ToListAsync(ct);

            foreach (var p in matches)
            {
                if (p.TryGetValue("_id", out var idv) && !idv.IsBsonNull) keys.Add(idv.ToString()!);
                if (p.TryGetValue("patient_code", out var pcv) && pcv.IsString && !string.IsNullOrWhiteSpace(pcv.AsString)) keys.Add(pcv.AsString);
                if (p.TryGetValue("patientCode", out var pc2) && pc2.IsString && !string.IsNullOrWhiteSpace(pc2.AsString)) keys.Add(pc2.AsString);
            }
        }
        catch {  }

        return keys.ToList();
    }

    private async Task<TestResult?> FindBestTestResultForSampleAsync(Sample? sample, CancellationToken ct)
    {
        if (sample == null) return null;

        var keys = new[] { sample.Id, sample.Barcode }
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (keys.Count == 0) return null;

        try
        {
            var raw = _database.GetCollection<BsonDocument>("test_results");
            var f = Builders<BsonDocument>.Filter;
            var idValues = new BsonArray();
            foreach (var k in keys)
            {
                idValues.Add(k);
                if (ObjectId.TryParse(k, out var oid)) idValues.Add(oid);
            }

            var filter = f.Or(
                f.In("sampleId", idValues),
                f.In("sample_id", idValues),
                f.In("barcode", idValues),
                f.In("Barcode", idValues));

            var docs = await raw.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("createdAt").Descending("created_at"))
                .Limit(20)
                .ToListAsync(ct);

            if (docs.Count == 0)
            {
                var typed = await _resultsCollection
                    .Find(Builders<TestResult>.Filter.In(x => x.SampleId, keys))
                    .SortByDescending(x => x.CreatedAt)
                    .ToListAsync(ct);
                if (typed.Count == 0) return null;
                return PreferApprovedResult(typed);
            }

            BsonDocument? best = docs.FirstOrDefault(d =>
            {
                var status = d.GetValue("status", "").ToString() ?? "";
                var validation = d.GetValue("validationStatus", d.GetValue("validation_status", "")).ToString() ?? "";
                var sig = d.GetValue("digitalSignature", d.GetValue("digital_signature", BsonNull.Value));
                var approved = status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)
                    || validation is "3" or "DoctorApproved";
                var hasSig = sig.IsString && !string.IsNullOrWhiteSpace(sig.AsString);
                return approved || hasSig;
            }) ?? docs.First();

            var code = best.GetValue("resultCode", best.GetValue("result_code", BsonNull.Value));
            var id = best.GetValue("_id", BsonNull.Value).ToString() ?? "";
            var codeStr = code.IsString ? code.AsString : (code.IsBsonNull ? "" : code.ToString()!);

            TestResult? entity = null;
            if (!string.IsNullOrWhiteSpace(codeStr))
            {
                entity = await _resultsCollection
                    .Find(x => x.ResultCode == codeStr || x.Id == codeStr || x.TestResultId == codeStr)
                    .FirstOrDefaultAsync(ct);
            }
            if (entity == null && !string.IsNullOrWhiteSpace(id))
            {
                entity = await _resultsCollection.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
            }

            if (entity != null)
            {
                if (string.IsNullOrWhiteSpace(entity.ResultCode) && !string.IsNullOrWhiteSpace(codeStr))
                    entity.ResultCode = codeStr;
                if (string.IsNullOrWhiteSpace(entity.ResultCode) && id.StartsWith("TRE-", StringComparison.OrdinalIgnoreCase))
                    entity.ResultCode = id;
                if (string.IsNullOrWhiteSpace(entity.PackageId))
                    entity.PackageId = GetDocString(best, "packageId", "package_id");
                if (string.IsNullOrWhiteSpace(entity.DeviceId))
                    entity.DeviceId = GetDocString(best, "deviceId", "equipment_code");
                if (string.IsNullOrWhiteSpace(entity.EnteredByUserId))
                    entity.EnteredByUserId = GetDocString(best, "enteredByUserId", "technician_id", "entered_by");
                if (string.IsNullOrWhiteSpace(entity.ApprovedByDoctorId))
                    entity.ApprovedByDoctorId = GetDocString(best, "approvedByDoctorId", "approved_by", "approvedBy");
                if (string.IsNullOrWhiteSpace(entity.ReviewedBy))
                    entity.ReviewedBy = GetDocString(best, "reviewedBy", "reviewed_by");
                return entity;
            }

            return new TestResult
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id,
                ResultCode = !string.IsNullOrWhiteSpace(codeStr) ? codeStr : id,
                SampleId = sample.Id,
                PackageId = GetDocString(best, "packageId", "package_id"),
                DeviceId = GetDocString(best, "deviceId", "equipment_code"),
                EnteredByUserId = GetDocString(best, "enteredByUserId", "technician_id", "entered_by"),
                ApprovedByDoctorId = GetDocString(best, "approvedByDoctorId", "approved_by", "approvedBy"),
                ReviewedBy = GetDocString(best, "reviewedBy", "reviewed_by"),
                Status = best.GetValue("status", "Approved").ToString() ?? "Approved",
                DigitalSignature = best.GetValue("digitalSignature", best.GetValue("digital_signature", BsonNull.Value)) is var s && s.IsString ? s.AsString : null,
                ApprovedAt = best.GetValue("approvedAt", best.GetValue("approved_at", BsonNull.Value)) is var a && a.IsBsonDateTime ? a.ToUniversalTime() : null,
                CreatedAt = best.GetValue("createdAt", best.GetValue("created_at", BsonNull.Value)) is var c && c.IsBsonDateTime ? c.ToUniversalTime() : DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    private static TestResult PreferApprovedResult(IReadOnlyList<TestResult> candidates) =>
        candidates.FirstOrDefault(r =>
            string.Equals(r.Status, "Approved", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) ||
            r.ValidationStatus == ResultValidationStatus.DoctorApproved ||
            !string.IsNullOrWhiteSpace(r.DigitalSignature))
        ?? candidates[0];

    private async Task<TrackProgressResponse> BuildEnrichedTrackingResponseAsync(
        TestOrder? order,
        Sample? sample,
        TestResult? result,
        bool isPublicQuery,
        CancellationToken ct)
    {
        var patientId = order?.PatientId ?? sample?.PatientId ?? string.Empty;
        string? resolvedPatientName = order?.PatientName ?? sample?.PatientName;
        string? resolvedGender = order?.PatientGender ?? sample?.PatientGender;
        int? resolvedAge = order?.PatientAge ?? sample?.PatientAge;
        string? resolvedPackageName = sample?.TestPackageName;

        if (!string.IsNullOrWhiteSpace(patientId))
        {
            try
            {
                var patientsCol = _database.GetCollection<BsonDocument>("patients");
                var pf = Builders<BsonDocument>.Filter;
                var patDoc = await patientsCol.Find(pf.Or(
                    pf.Eq("_id", patientId),
                    pf.Eq("patient_code", patientId),
                    pf.Eq("patientCode", patientId))).FirstOrDefaultAsync(ct);

                if (patDoc != null)
                {
                    if (string.IsNullOrWhiteSpace(resolvedPatientName))
                        resolvedPatientName = GetDocString(patDoc, "fullname", "fullName", "FullName");

                    var patGender = GetDocString(patDoc, "gender", "Gender");
                    if (!string.IsNullOrWhiteSpace(patGender))
                        resolvedGender = patGender;

                    if (patDoc.TryGetValue("dob", out var dobVal) && dobVal.IsBsonDateTime)
                    {
                        resolvedAge = ComputeAge(dobVal.ToUniversalTime());
                    }
                    else if (patDoc.TryGetValue("dateOfBirth", out var dobVal2) && dobVal2.IsBsonDateTime)
                    {
                        resolvedAge = ComputeAge(dobVal2.ToUniversalTime());
                    }
                }
            }
            catch {  }
        }

        if (string.IsNullOrWhiteSpace(resolvedPackageName))
        {
            var pkgKey = result?.PackageId;
            if (string.IsNullOrWhiteSpace(pkgKey) && !string.IsNullOrWhiteSpace(sample?.TestOrderId))
            {
                try
                {
                    var apptCol = _database.GetCollection<BsonDocument>("appointments");
                    var af = Builders<BsonDocument>.Filter;
                    var apptDoc = await apptCol.Find(af.Or(
                        af.Eq("_id", sample.TestOrderId),
                        af.Eq("appointmentCode", sample.TestOrderId))).FirstOrDefaultAsync(ct);
                    if (apptDoc != null)
                    {
                        pkgKey = GetDocString(apptDoc, "packageId", "package_id", "PackageId");
                        resolvedPackageName = GetDocString(apptDoc, "packageName", "package_name");
                    }
                }
                catch {  }
            }

            if (string.IsNullOrWhiteSpace(resolvedPackageName) && !string.IsNullOrWhiteSpace(pkgKey))
            {
                resolvedPackageName = await ResolvePackageNameAsync(pkgKey, ct) ?? pkgKey;
            }
        }

        var handlers = await ResolveStepHandlersAsync(order, sample, result, ct);
        return BuildTrackingResponse(order, sample, result, isPublicQuery, resolvedPatientName, resolvedGender, resolvedAge, resolvedPackageName, handlers);
    }

    private sealed record ResolvedStepHandlers(
        string? Step1Handler,
        string? Step2Handler,
        string? Step3Handler,
        string? Step4Handler,
        string? Step5Handler,
        string? Step6Handler);

    private async Task<ResolvedStepHandlers> ResolveStepHandlersAsync(
        TestOrder? order,
        Sample? sample,
        TestResult? result,
        CancellationToken ct)
    {
        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        async Task<string?> PickFirstResolvedAsync(params string?[] candidates)
        {
            foreach (var raw in candidates)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var resolved = await ResolveStaffDisplayNameAsync(raw, cache, ct);
                if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
            }
            return null;
        }

        string? HistoryActor(params string[] targetStatuses)
        {
            if (sample?.StatusHistory == null || sample.StatusHistory.Count == 0) return null;
            var hit = sample.StatusHistory
                .Where(h => targetStatuses.Any(s => string.Equals(h.ToStatus, s, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(h => h.ChangedAt)
                .FirstOrDefault();
            if (hit == null) return null;
            return !string.IsNullOrWhiteSpace(hit.OperatorName) ? hit.OperatorName : hit.ChangedBy;
        }

        var linkedIds = new[] { order?.AppointmentId, sample?.TestOrderId, order?.Id, result?.AppointmentId }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? appointmentDoctorCandidate = null;
        string? assessmentDoctorCandidate = null;
        string? commentAuthorCandidate = null;
        string? paymentCashierCandidate = null;

        if (linkedIds.Count > 0)
        {
            var idValues = new BsonArray();
            foreach (var id in linkedIds)
            {
                idValues.Add(id);
                if (ObjectId.TryParse(id, out var oid)) idValues.Add(oid);
            }

            try
            {
                var apptCol = _database.GetCollection<BsonDocument>("appointments");
                var af = Builders<BsonDocument>.Filter;
                var apptDoc = await apptCol.Find(af.Or(
                    af.In("_id", idValues),
                    af.In("appointmentCode", idValues),
                    af.In("appointment_code", idValues))).FirstOrDefaultAsync(ct);
                if (apptDoc != null)
                {
                    appointmentDoctorCandidate = GetDocString(apptDoc, "doctorName", "doctor_name", "doctorId", "doctor_id", "createdBy", "created_by");
                }
            }
            catch {  }

            if (string.IsNullOrWhiteSpace(order?.DoctorName) && string.IsNullOrWhiteSpace(order?.DoctorId) && string.IsNullOrWhiteSpace(appointmentDoctorCandidate))
            {
                try
                {
                    var maCol = _database.GetCollection<BsonDocument>("medical_assessments");
                    var mf = Builders<BsonDocument>.Filter;
                    var maDoc = await maCol.Find(mf.Or(
                        mf.In("appointment_id", idValues),
                        mf.In("appointmentId", idValues),
                        mf.In("test_order_id", idValues),
                        mf.In("testOrderId", idValues))).FirstOrDefaultAsync(ct);
                    if (maDoc != null)
                    {
                        assessmentDoctorCandidate = GetDocString(maDoc, "doctor_name", "doctorName", "doctor_id", "doctorId");
                    }
                }
                catch {  }

                try
                {
                    var cmtCol = _database.GetCollection<BsonDocument>("test_order_comments");
                    var cf = Builders<BsonDocument>.Filter;
                    var cmtDoc = await cmtCol.Find(cf.Or(
                        cf.In("appointment_id", idValues),
                        cf.In("appointmentId", idValues),
                        cf.In("test_order_id", idValues),
                        cf.In("testOrderId", idValues))).FirstOrDefaultAsync(ct);
                    if (cmtDoc != null)
                    {
                        commentAuthorCandidate = GetDocString(cmtDoc, "author_name", "authorName", "author_id", "authorId");
                    }
                }
                catch {  }

                try
                {
                    var payCol = _database.GetCollection<BsonDocument>("payments");
                    var pf = Builders<BsonDocument>.Filter;
                    var payDoc = await payCol.Find(pf.Or(
                        pf.In("appointment_id", idValues),
                        pf.In("appointmentId", idValues))).FirstOrDefaultAsync(ct);
                    if (payDoc != null)
                    {
                        paymentCashierCandidate = GetDocString(payDoc, "cashier_name", "cashierName", "cashier_id", "cashierId");
                    }
                }
                catch {  }
            }
        }

        var step1 = await PickFirstResolvedAsync(
            order?.DoctorName,
            order?.DoctorId,
            appointmentDoctorCandidate,
            assessmentDoctorCandidate,
            commentAuthorCandidate,
            paymentCashierCandidate);

        var step2 = await PickFirstResolvedAsync(
            sample?.CollectorName,
            sample?.CollectedBy,
            HistoryActor("Collected", "InTransit"),
            sample?.HandoverBy,
            sample?.HandoverById);

        var isRejected = sample?.Status == SampleStatus.Rejected || !string.IsNullOrEmpty(sample?.RejectionReason);
        var step3 = await PickFirstResolvedAsync(
            isRejected ? sample?.RejectedBy : null,
            sample?.ReceivedBy,
            HistoryActor("Received"),
            sample?.AssignedTechnicianName,
            sample?.AssignedTechnicianId,
            result?.EnteredByUserId);

        var step4 = await PickFirstResolvedAsync(
            result?.EnteredByUserId,
            sample?.AssignedTechnicianName,
            sample?.AssignedTechnicianId,
            HistoryActor("Processing", "Testing", "Completed"),
            step3);

        var step5 = await PickFirstResolvedAsync(
            result?.ApprovedByDoctorId,
            result?.ReviewedBy,
            order?.DoctorName,
            order?.DoctorId,
            step1);

        var step6 = await PickFirstResolvedAsync(
            result?.ApprovedByDoctorId,
            result?.ReviewedBy,
            step5);

        return new ResolvedStepHandlers(step1, step2, step3, step4, step5, step6);
    }

    private async Task<string?> ResolveStaffDisplayNameAsync(string? raw, Dictionary<string, string?> cache, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var key = raw.Trim();
        if (cache.TryGetValue(key, out var cached)) return cached;

        string? resolved = null;
        var idValues = new BsonArray { key };
        if (ObjectId.TryParse(key, out var oid)) idValues.Add(oid);

        foreach (var colName in new[] { "accounts", "employees" })
        {
            try
            {
                var col = _database.GetCollection<BsonDocument>(colName);
                var f = Builders<BsonDocument>.Filter;
                var doc = await col.Find(f.Or(
                    f.In("_id", idValues),
                    f.In("accountId", idValues),
                    f.In("account_id", idValues),
                    f.In("userId", idValues),
                    f.Eq("username", key),
                    f.Eq("employeeCode", key),
                    f.Eq("employee_code", key))).FirstOrDefaultAsync(ct);

                if (doc != null)
                {
                    resolved = GetDocString(doc, "fullName", "FullName", "fullname", "displayName", "name", "username");
                    if (!string.IsNullOrWhiteSpace(resolved)) break;
                }
            }
            catch {  }
        }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            bool looksLikeOpaqueId = ObjectId.TryParse(key, out _)
                || Guid.TryParse(key, out _)
                || System.Text.RegularExpressions.Regex.IsMatch(key, @"^[A-Z]{2,8}[-_][A-Z0-9-]+$");
            if (!looksLikeOpaqueId)
            {
                resolved = key;
            }
        }

        cache[key] = resolved;
        return resolved;
    }

    private async Task<string?> ResolvePackageNameAsync(string pkgKey, CancellationToken ct)
    {
        foreach (var colName in new[] { "service_packages", "test_packages" })
        {
            try
            {
                var col = _database.GetCollection<BsonDocument>(colName);
                var f = Builders<BsonDocument>.Filter;
                var filters = new List<FilterDefinition<BsonDocument>>
                {
                    f.Eq("_id", pkgKey),
                    f.Eq("Code", pkgKey),
                    f.Eq("package_code", pkgKey),
                    f.Eq("code", pkgKey)
                };
                if (ObjectId.TryParse(pkgKey, out var oid)) filters.Add(f.Eq("_id", oid));
                var doc = await col.Find(f.Or(filters)).FirstOrDefaultAsync(ct);
                if (doc != null)
                {
                    var name = GetDocString(doc, "Name", "package_name", "packageName", "name");
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
            catch {  }
        }
        return null;
    }

    private static int ComputeAge(DateTime dobUtc)
    {
        var today = DateTime.UtcNow.Date;
        var age = today.Year - dobUtc.Year;
        if (dobUtc.Date > today.AddYears(-age)) age--;
        return Math.Max(0, age);
    }

    private static string? GetDocString(BsonDocument doc, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!doc.TryGetValue(k, out var v) || v.IsBsonNull) continue;
            if (v.IsString && !string.IsNullOrWhiteSpace(v.AsString))
                return v.AsString.Trim();
            if (v.IsObjectId)
                return v.AsObjectId.ToString();
        }
        return null;
    }

    private TrackProgressResponse BuildTrackingResponse(
        TestOrder? order,
        Sample? sample,
        TestResult? result,
        bool isPublicQuery,
        string? resolvedPatientName,
        string? resolvedGender,
        int? resolvedAge,
        string? resolvedPackageName,
        ResolvedStepHandlers handlers)
    {
        var orderNumber = !string.IsNullOrWhiteSpace(order?.OrderNumber)
            ? order!.OrderNumber
            : (!string.IsNullOrWhiteSpace(sample?.Barcode)
                ? sample!.Barcode!
                : (sample?.Id ?? "ORD-UNKNOWN"));
        var patientName = !string.IsNullOrWhiteSpace(resolvedPatientName)
            ? resolvedPatientName
            : (order?.PatientName ?? sample?.PatientName ?? "Bệnh nhân");
        if (isPublicQuery)
        {
            patientName = MaskPatientName(patientName);
        }

        var patientId = order?.PatientId ?? sample?.PatientId ?? string.Empty;
        if (isPublicQuery && !string.IsNullOrWhiteSpace(patientId))
        {
            patientId = patientId.Length > 8
                ? $"{patientId[..4]}****{patientId[^3..]}"
                : "PAT-****";
        }
        var orderedAt = order?.CreatedAt ?? sample?.CollectedAt ?? DateTime.UtcNow;
        var priority = order?.Priority.ToString() ?? sample?.Priority.ToString() ?? "Normal";

        var isStat = priority.Equals("Stat", StringComparison.OrdinalIgnoreCase) || priority.Equals("Urgent", StringComparison.OrdinalIgnoreCase);
        var expectedDurationMinutes = isStat ? 60 : 120;
        var estimatedCompletionTime = orderedAt.AddMinutes(expectedDurationMinutes);

        var packages = order?.Items?.Select(i => i.TestName).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList()
            ?? (string.IsNullOrWhiteSpace(resolvedPackageName) ? new List<string> { "Xét nghiệm tổng quát" } : new List<string> { resolvedPackageName });

        var isRejected = sample?.Status == SampleStatus.Rejected || (!string.IsNullOrEmpty(sample?.RejectionReason));
        var isRerun = !string.IsNullOrEmpty(sample?.RerunReason) || sample?.Status == SampleStatus.RerunRequested;

        var steps = new List<TrackingStepDto>();

        steps.Add(new TrackingStepDto
        {
            StepNumber = 1,
            StepCode = "REGISTERED",
            Title = "Tiếp Nhận & Đăng Ký Chỉ Định",
            Description = "Hồ sơ xét nghiệm đã được tạo và chỉ định bởi Bác sĩ lâm sàng.",
            Status = TrackingStepStatus.Completed,
            StatusText = "Đã hoàn thành",
            Timestamp = orderedAt,
            HandlerName = handlers.Step1Handler,
            Department = "Phòng Khám Lâm Sàng"
        });

        bool step2Done = (sample != null && sample.CollectedAt != default) || (order != null && TestOrderStatusRules.IsSampleCollectedOrLater(order.Status));
        steps.Add(new TrackingStepDto
        {
            StepNumber = 2,
            StepCode = "SAMPLE_COLLECTED",
            Title = "Lấy Mẫu Máu & Dán Barcode",
            Description = "Điều dưỡng thực hiện lấy mẫu vô trùng và dán tem mã vạch Barcode Code 128.",
            Status = step2Done ? TrackingStepStatus.Completed : TrackingStepStatus.InProgress,
            StatusText = step2Done ? "Đã lấy mẫu" : "Đang chờ lấy mẫu",
            Timestamp = step2Done ? (sample?.CollectedAt ?? orderedAt.AddMinutes(15)) : null,
            HandlerName = step2Done ? handlers.Step2Handler : null,
            Department = "Phòng Lấy Mẫu Bệnh Phẩm"
        });

        bool step3Done = false;
        var step3Status = TrackingStepStatus.Pending;
        string step3Text = "Chờ nhập Lab";
        string? step3Notes = null;

        if (isRejected)
        {
            step3Status = TrackingStepStatus.Rejected;
            step3Text = "Mẫu bị từ chối";
            step3Notes = sample?.RejectionReason ?? "Mẫu không đạt chất lượng y khoa (Huyết tán/Đông dây). Vui lòng liên hệ quầy lấy lại mẫu.";
        }
        else if (sample != null && (sample.ReceivedAt.HasValue || sample.Status >= SampleStatus.Received))
        {
            step3Done = true;
            step3Status = TrackingStepStatus.Completed;
            step3Text = "Đã nhập Lab";
        }
        else if (order != null && order.Status >= TestOrderStatus.InAnalysis)
        {
            step3Done = true;
            step3Status = TrackingStepStatus.Completed;
            step3Text = "Đã nhập Lab";
        }
        else if (step2Done)
        {
            step3Status = TrackingStepStatus.InProgress;
            step3Text = "Đang vận chuyển";
        }

        steps.Add(new TrackingStepDto
        {
            StepNumber = 3,
            StepCode = "LAB_RECEIVED",
            Title = "Tiếp Nhận Phòng Xét Nghiệm (LIS)",
            Description = "Mẫu đã tới phòng xét nghiệm, hệ thống LIS kiểm tra thể tích và quét nhận mẫu.",
            Status = step3Status,
            StatusText = step3Text,
            Timestamp = step3Done ? (sample?.ReceivedAt ?? (sample?.CollectedAt.AddMinutes(10) ?? orderedAt.AddMinutes(25))) : null,
            HandlerName = (step3Done || isRejected) ? handlers.Step3Handler : null,
            Department = "Khoa Xét Nghiệm Trung Tâm",
            Notes = step3Notes
        });

        bool step4Done = false;
        var step4Status = TrackingStepStatus.Pending;
        string step4Text = "Chờ phân tích";
        string? step4Notes = null;

        if (isRerun)
        {
            step4Status = TrackingStepStatus.Warning;
            step4Text = "Đang chạy lại mẫu (Rerun)";
            step4Notes = sample?.RerunReason ?? "Cần chạy lại kiểm chứng chỉ số nghi ngờ.";
        }
        else if (result != null || (sample != null && (sample.ProcessedAt.HasValue || sample.Status >= SampleStatus.Completed)) || (order != null && order.Status >= TestOrderStatus.Completed))
        {
            step4Done = true;
            step4Status = TrackingStepStatus.Completed;
            step4Text = "Đã phân tích";
        }
        else if (step3Done)
        {
            step4Status = TrackingStepStatus.InProgress;
            step4Text = "Đang phân tích máy";
        }

        steps.Add(new TrackingStepDto
        {
            StepNumber = 4,
            StepCode = "ANALYZING",
            Title = "Phân Tích Thiết Bị Tự Động",
            Description = "Mẫu được phân tích trên máy Cobas / Sysmex thế hệ mới với nội kiểm IQC đạt chuẩn.",
            Status = step4Status,
            StatusText = step4Text,
            Timestamp = step4Done ? (sample?.ProcessedAt ?? result?.EnteredAt ?? result?.CreatedAt ?? orderedAt.AddMinutes(50)) : null,
            HandlerName = (step4Done || isRerun || step4Status == TrackingStepStatus.InProgress) ? handlers.Step4Handler : null,
            Department = "Phòng Máy Tự Động Hóa",
            Notes = step4Notes
        });

        bool isApprovedResult = result != null &&
            !result.IsSuperseded &&
            (string.Equals(result.Status, "Approved", StringComparison.OrdinalIgnoreCase) ||
             result.ValidationStatus == ResultValidationStatus.DoctorApproved ||
             !string.IsNullOrWhiteSpace(result.DigitalSignature));

        bool step5Done = isApprovedResult
            || (order != null && order.Status == TestOrderStatus.Completed);

        var step5Status = step5Done
            ? TrackingStepStatus.Completed
            : (step4Done ? TrackingStepStatus.InProgress : TrackingStepStatus.Pending);
        var step5Text = step5Done
            ? "Đã thẩm định"
            : (step4Done ? "Đang thẩm định y khoa" : "Chờ thẩm định");

        steps.Add(new TrackingStepDto
        {
            StepNumber = 5,
            StepCode = "MEDICAL_REVIEW",
            Title = "Thẩm Định Chuyên Môn Y Khoa",
            Description = "Bác sĩ Chuyên khoa xét nghiệm đánh giá dải tham chiếu, kết luận lâm sàng.",
            Status = step5Status,
            StatusText = step5Text,
            Timestamp = step5Done ? (result?.ApprovedAt ?? sample?.CompletedAt ?? result?.CreatedAt ?? orderedAt.AddMinutes(75)) : null,
            HandlerName = step5Done ? handlers.Step5Handler : null,
            Department = "Hội Đồng Thẩm Định Y Khoa"
        });

        bool step6Done = step5Done;
        var step6Status = step6Done ? TrackingStepStatus.Completed : (step4Done ? TrackingStepStatus.InProgress : TrackingStepStatus.Pending);

        steps.Add(new TrackingStepDto
        {
            StepNumber = 6,
            StepCode = "COMPLETED",
            Title = "Phát Hành Kết Quả Xét Nghiệm",
            Description = "Kết quả đã được Bác sĩ phê duyệt, sẵn sàng xem trực tuyến hoặc tải phiếu kết quả.",
            Status = step6Status,
            StatusText = step6Done ? "Đã có kết quả" : "Chờ phát hành",
            Timestamp = step6Done ? (result?.ApprovedAt ?? sample?.CompletedAt ?? result?.CreatedAt ?? orderedAt.AddMinutes(90)) : null,
            HandlerName = step6Done ? handlers.Step6Handler : null,
            Department = "Cổng Kết Quả Bệnh Nhân"
        });

        int currentStepNum = 1;
        string currentStepName = "Tiếp Nhận";
        if (isRejected)
        {
            currentStepNum = 3;
            currentStepName = "Mẫu Bị Từ Chối";
        }
        else if (step6Done)
        {
            currentStepNum = 6;
            currentStepName = "Đã Có Kết Quả";
        }
        else if (step5Done)
        {
            currentStepNum = 6;
            currentStepName = "Đang Phát Hành Kết Quả";
        }
        else if (step4Done)
        {
            currentStepNum = 5;
            currentStepName = "Đang Thẩm Định Y Khoa";
        }
        else if (step3Done)
        {
            currentStepNum = 4;
            currentStepName = "Đang Phân Tích Thiết Bị";
        }
        else if (step2Done)
        {
            currentStepNum = 3;
            currentStepName = "Mẫu Đang Vận Chuyển Đến Lab";
        }
        else
        {
            currentStepNum = 2;
            currentStepName = "Chờ Lấy Mẫu Máu";
        }

        var elapsedMinutes = Math.Round((DateTime.UtcNow - orderedAt).TotalMinutes, 1);

        return new TrackProgressResponse
        {
            OrderNumber = orderNumber,
            Barcode = sample?.Barcode ?? orderNumber,
            ResultCode = string.IsNullOrWhiteSpace(result?.ResultCode) ? null : result!.ResultCode,
            PatientId = patientId,
            PatientName = patientName,
            PatientGender = string.IsNullOrWhiteSpace(resolvedGender) ? "Chưa rõ" : resolvedGender,
            PatientAge = resolvedAge ?? 0,
            Priority = priority,
            TestPackageNames = packages,
            OverallStatus = isRejected ? "Rejected" : (step6Done ? "Completed" : "Processing"),
            CurrentStepNumber = currentStepNum,
            CurrentStepName = currentStepName,
            OrderedAt = orderedAt,
            EstimatedCompletionTime = estimatedCompletionTime,
            ElapsedMinutes = Math.Max(0, elapsedMinutes),
            IsCompleted = step6Done,
            IsRejected = isRejected,
            RejectionReason = sample?.RejectionReason,
            IsRerunRequested = isRerun,
            RerunReason = sample?.RerunReason,
            Steps = steps
        };
    }

    private static string MaskPatientName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return "Bệnh nhân";

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
        {
            if (fullName.Length <= 2) return fullName;
            return fullName[0] + new string('*', fullName.Length - 2) + fullName[^1];
        }

        var masked = new List<string> { parts[0] };
        for (int i = 1; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Length <= 1) masked.Add(p);
            else masked.Add(p[0] + new string('*', Math.Max(1, p.Length - 1)));
        }
        return string.Join(" ", masked);
    }
}
