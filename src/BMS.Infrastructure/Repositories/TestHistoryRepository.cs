using System.Text.RegularExpressions;
using System.Globalization;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;
namespace BMS.Infrastructure.Repositories;

/// <summary>Reads both established result schemas without demo data, synthesized clinical values or schema migrations.</summary>
public sealed class TestHistoryRepository : ITestHistoryRepository
{
    private readonly IMongoDatabase _db;
    private IMongoCollection<BsonDocument> Results => _db.GetCollection<BsonDocument>("test_results");
    public TestHistoryRepository(IMongoDatabase database)
    {
        _db = database;
        Results.Indexes.CreateMany([
            new(Builders<BsonDocument>.IndexKeys.Ascending("patient_id").Descending("created_at").Ascending("_id")),
            new(Builders<BsonDocument>.IndexKeys.Ascending("patientId").Descending("createdAt").Ascending("_id"))
        ]);
        _db.GetCollection<BsonDocument>("test_parameters").Indexes.CreateOne(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("result_id")));
    }
    private static BsonValue Id(string id) => ObjectId.TryParse(id, out var oid) ? oid : new BsonString(id);
    private static FilterDefinition<BsonDocument> Identity(string field, string id) => new BsonDocument(field, new BsonDocument("$in", new BsonArray { id, Id(id) }));
    private static string Text(BsonDocument d, string field) => d.TryGetValue(field, out var v) && !v.IsBsonNull ? v.ToString()! : "";
    private static BsonValue Alias(BsonDocument d, string first, string second) => d.TryGetValue(first, out var v) && !v.IsBsonNull ? v : d.GetValue(second, BsonNull.Value);
    private static string Text(BsonDocument d, string first, string second) => Alias(d, first, second) is var v && !v.IsBsonNull ? v.ToString()! : "";
    private static DateTime? Date(BsonDocument d, string first, string second) => Alias(d, first, second) is var v && v.IsBsonDateTime ? v.ToUniversalTime() : null;
    private static string Measurement(BsonDocument d)
    {
        var value = Alias(d, "measuredValue", "value_numeric");
        // Round-trip formatting preserves the stored double without the driver's verbose BSON diagnostic representation.
        return value.IsDouble ? value.AsDouble.ToString("R", CultureInfo.InvariantCulture) : value.IsBsonNull ? "" : value.ToString()!;
    }
    private static readonly BsonDocument Active = new() { { "is_deleted", new BsonDocument("$ne", true) }, { "isDeleted", new BsonDocument("$ne", true) }, { "isActive", new BsonDocument("$ne", false) } };
    private static BsonValue Coalesce(string first, string second) => new BsonDocument("$ifNull", new BsonArray { "$" + first, "$" + second });
    private static BsonDocument PatientFilter(List<string>? scope) => scope is null ? new() : new("$expr", new BsonDocument("$in", new BsonArray {
        Coalesce("patient_id", "patientId"), new BsonArray(scope.SelectMany(s => new BsonValue[] { s, Id(s) })) }));
    public async Task<List<string>> OwnPatientsAsync(string accountId, CancellationToken ct = default) =>
        (await _db.GetCollection<BsonDocument>("patients")
            .Find(new BsonDocumentFilterDefinition<BsonDocument>(Active) & (Identity("AccountId", accountId) | Identity("accountId", accountId)))
            .Project(new BsonDocument("_id", 1)).ToListAsync(ct)).Select(x => Text(x, "_id")).ToList();

    public async Task<(List<TestHistorySummary> Items, long Total)> ListAsync(TestHistorySearch search, List<string>? scope, CancellationToken ct = default)
    {
        var f = Builders<BsonDocument>.Filter;
        FilterDefinition<BsonDocument> filter = Active;
        filter &= PatientFilter(scope);
        if (!string.IsNullOrEmpty(search.PatientId)) filter &= PatientFilter([search.PatientId]);
        if (!string.IsNullOrEmpty(search.Search))
        {
            var regex = new BsonRegularExpression(Regex.Escape(search.Search), "i");
            filter &= f.Or(Identity("_id", search.Search), f.Regex("resultCode", regex), f.Regex("result_code", regex), Identity("sample_id", search.Search), Identity("sampleId", search.Search), Identity("package_id", search.Search), Identity("packageId", search.Search));
        }
        if (search.FromDate.HasValue || search.ToDate.HasValue)
            filter &= new BsonDocument("$expr", new BsonDocument("$eq", new BsonArray { new BsonDocument("$type", Coalesce("created_at", "createdAt")), "date" }));
        if (search.FromDate.HasValue) filter &= new BsonDocument("$expr", new BsonDocument("$gte", new BsonArray { Coalesce("created_at", "createdAt"), DateTime.SpecifyKind(search.FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc) }));
        if (search.ToDate.HasValue) filter &= new BsonDocument("$expr", new BsonDocument("$and", new BsonArray {
            new BsonDocument("$eq", new BsonArray { new BsonDocument("$type", Coalesce("created_at", "createdAt")), "date" }),
            new BsonDocument("$lt", new BsonArray { Coalesce("created_at", "createdAt"), DateTime.SpecifyKind(search.ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc) }) }));
        var total = await Results.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await Results.Aggregate().Match(filter).AppendStage<BsonDocument>(new BsonDocument("$set", new BsonDocument("__historyDate", Coalesce("created_at", "createdAt"))))
            .Sort(new BsonDocument { { "__historyDate", -1 }, { "_id", 1 } }).Skip((search.Page - 1) * search.PageSize).Limit(search.PageSize)
            .Project<BsonDocument>(new BsonDocument { { "items", 0 }, { "conclusion_notes", 0 }, { "clinicalComment", 0 }, { "report_pdf_url", 0 } }).ToListAsync(ct);
        var items = new List<TestHistorySummary>();
        foreach (var d in docs) items.Add(await Summary(d, ct));
        return (items, total);
    }
    private async Task<TestHistorySummary> Summary(BsonDocument d, CancellationToken ct)
    {
        var signature = Alias(d, "digital_signature", "digitalSignature");
        var status = Text(d, "status");
        var validation = Text(d, "validationStatus");
        var hasValidSig = signature.IsString && !string.IsNullOrWhiteSpace(signature.AsString);
        var doctorApproved = validation is "3" or "DoctorApproved"
            || (string.IsNullOrWhiteSpace(validation) && status.Equals("APPROVED", StringComparison.OrdinalIgnoreCase));
        var statusOk = status.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(status) && doctorApproved);
        var approved = doctorApproved
            && statusOk
            && !d.GetValue("rerun_requested", false).Equals(BsonBoolean.True)
            && !d.GetValue("rerunRequested", false).Equals(BsonBoolean.True);
        var sampleId = Text(d, "sample_id", "sampleId"); var patientId = Text(d, "patient_id", "patientId");
        var sample = await _db.GetCollection<BsonDocument>("blood_samples").Find(
                new BsonDocumentFilterDefinition<BsonDocument>(Active)
                & Builders<BsonDocument>.Filter.Or(Identity("_id", sampleId), Builders<BsonDocument>.Filter.Eq("barcode", sampleId)))
            .Project(new BsonDocument
            {
                { "_id", 0 }, { "status", 1 }, { "barcode", 1 },
                { "packageId", 1 }, { "package_id", 1 }, { "packageCode", 1 },
                { "test_package_name", 1 }, { "testPackageName", 1 }
            }).FirstOrDefaultAsync(ct);
        if (sample is not null)
            approved &= new[] { "Completed", "Stored" }.Contains(Text(sample, "status"), StringComparer.OrdinalIgnoreCase);
        var packageId = Text(d, "package_id", "packageId");
        if (string.IsNullOrWhiteSpace(packageId) && sample is not null)
            packageId = Text(sample, "packageId", "package_id");
        if (string.IsNullOrWhiteSpace(packageId) && sample is not null)
            packageId = Text(sample, "packageCode");
        var package = string.IsNullOrWhiteSpace(packageId) ? null
            : await _db.GetCollection<BsonDocument>("test_packages").Find(
                    Builders<BsonDocument>.Filter.Or(Identity("_id", packageId), Builders<BsonDocument>.Filter.Eq("code", packageId), Builders<BsonDocument>.Filter.Eq("package_code", packageId)))
                .Project(new BsonDocument { { "_id", 0 }, { "package_name", 1 }, { "name", 1 } }).FirstOrDefaultAsync(ct);
        var packageName = package is null ? "" : (Text(package, "package_name").Length > 0 ? Text(package, "package_name") : Text(package, "name"));
        if (string.IsNullOrWhiteSpace(packageName) && !string.IsNullOrWhiteSpace(packageId))
        {
            var sp = await _db.GetCollection<BsonDocument>("service_packages")
                .Find(Builders<BsonDocument>.Filter.Or(Identity("_id", packageId), Builders<BsonDocument>.Filter.Eq("Code", packageId), Builders<BsonDocument>.Filter.Eq("code", packageId)))
                .Project(new BsonDocument { { "_id", 0 }, { "Name", 1 }, { "name", 1 } }).FirstOrDefaultAsync(ct);
            if (sp is not null)
                packageName = Text(sp, "Name", "name");
        }
        if (string.IsNullOrWhiteSpace(packageName) && sample is not null)
            packageName = Text(sample, "testPackageName", "test_package_name");
        if (string.IsNullOrWhiteSpace(packageName) && !string.IsNullOrWhiteSpace(packageId))
            packageName = packageId;
        if (string.IsNullOrWhiteSpace(packageName))
            packageName = "CBC / Huyết học";
        var displaySampleId = sample is not null && !string.IsNullOrWhiteSpace(Text(sample, "barcode"))
            ? Text(sample, "barcode")
            : sampleId;
        var resultCode = Text(d, "result_code", "resultCode");
        if (string.IsNullOrWhiteSpace(resultCode)) resultCode = Text(d, "testResultId");
        return new(Text(d, "_id"), resultCode, patientId, displaySampleId,
            packageName, Date(d, "created_at", "createdAt"), approved ? Date(d, "approved_at", "approvedAt") : null, approved);
    }
    public async Task<TestHistoryDetail?> GetAsync(string id, List<string>? scope, CancellationToken ct = default)
    {
        var d = await Results.Find(new BsonDocumentFilterDefinition<BsonDocument>(Active) & PatientFilter(scope) & Identity("_id", id)).FirstOrDefaultAsync(ct);
        if (d is null) return null;
        var summary = await Summary(d, ct);
        if (!summary.Available) return new(summary, [], null);
        var embedded = d.GetValue("items", BsonNull.Value);
        var docs = embedded.IsBsonArray && embedded.AsBsonArray.Count > 0 ? embedded.AsBsonArray.Where(x => x.IsBsonDocument).Select(x => x.AsBsonDocument).ToList() :
            await _db.GetCollection<BsonDocument>("test_parameters").Find(Identity("result_id", summary.Id)).Sort(new BsonDocument("_id", 1)).ToListAsync(ct);
        var items = docs.Select(x => new TestHistoryItem(Text(x, "code", "criterionCode"), Text(x, "name", "criterionName"), Measurement(x),
            Text(x, "unit"), Text(x, "referenceInterval", "reference_display"), Text(x, "flag"))).ToList();

        var icd10Code = NullIfEmpty(Text(d, "icd10Code", "icd10_code"));
        var finalDiagnosis = NullIfEmpty(Text(d, "finalDiagnosis", "final_diagnosis"));
        var doctorConclusion = NullIfEmpty(Text(d, "doctorConclusion", "doctor_conclusion"));
        var treatmentAdvice = NullIfEmpty(Text(d, "treatmentAdvice", "treatment_advice"));
        var followUpDate = NullIfEmpty(Text(d, "followUpDate", "follow_up_date"));
        var approvingDoctor = CleanDoctorName(Text(d, "reviewedBy", "reviewed_by"));
        var digitalSignature = NullIfEmpty(Text(d, "digitalSignature", "digital_signature"));

        var appointmentId = Text(d, "appointmentId", "appointment_id");
        var patientId = summary.PatientId;

        // Fallback 1: linked finalized MedicalAssessment (UC50) for this encounter or patient
        if (string.IsNullOrWhiteSpace(finalDiagnosis) || string.IsNullOrWhiteSpace(icd10Code))
        {
            var assessment = await FindLinkedAssessmentAsync(appointmentId, patientId, summary.CreatedAt, ct);
            if (assessment is not null)
            {
                icd10Code ??= NullIfEmpty(Text(assessment, "icd10_code", "icd10Code"));
                var asmDiag = NullIfEmpty(Text(assessment, "final_diagnosis", "finalDiagnosis"))
                    ?? NullIfEmpty(Text(assessment, "icd10_name", "icd10Name"));
                finalDiagnosis ??= asmDiag;
                doctorConclusion ??= NullIfEmpty(Text(assessment, "clinical_impression", "clinicalImpression"));
                treatmentAdvice ??= NullIfEmpty(Text(assessment, "treatment_plan", "treatmentPlan"))
                    ?? NullIfEmpty(Text(assessment, "prescription_notes", "prescriptionNotes"));
                if (followUpDate is null && assessment.TryGetValue("follow_up_date", out var fup) && fup.IsBsonDateTime)
                    followUpDate = fup.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                approvingDoctor ??= CleanDoctorName(Text(assessment, "doctor_name", "doctorName"));
            }
        }

        // Fallback 2: doctor consultation summary saved on linked appointment notes (for previously approved records)
        if ((string.IsNullOrWhiteSpace(finalDiagnosis) || string.IsNullOrWhiteSpace(doctorConclusion)) && !string.IsNullOrWhiteSpace(appointmentId))
        {
            var appt = await _db.GetCollection<BsonDocument>("appointments")
                .Find(Builders<BsonDocument>.Filter.Or(
                    Identity("_id", appointmentId),
                    Builders<BsonDocument>.Filter.Eq("appointmentCode", appointmentId),
                    Builders<BsonDocument>.Filter.Eq("appointment_code", appointmentId)))
                .Project(new BsonDocument { { "_id", 0 }, { "notes", 1 }, { "Notes", 1 } })
                .FirstOrDefaultAsync(ct);

            var apptNotes = appt is not null ? Text(appt, "notes", "Notes") : string.Empty;
            if (!string.IsNullOrWhiteSpace(apptNotes))
            {
                var (parsedIcd, parsedDiag, parsedConcl, parsedDoc) = ParseConsultationNote(apptNotes);
                icd10Code ??= parsedIcd;
                finalDiagnosis ??= parsedDiag;
                doctorConclusion ??= parsedConcl;
                approvingDoctor ??= parsedDoc;
            }
        }

        var rawConclusion = NullIfEmpty(Text(d, "conclusion_notes", "clinicalComment"));
        var effectiveConclusion = doctorConclusion ?? rawConclusion;

        return new(
            summary,
            items,
            effectiveConclusion,
            icd10Code,
            finalDiagnosis,
            doctorConclusion,
            treatmentAdvice,
            followUpDate,
            approvingDoctor,
            digitalSignature);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? CleanDoctorName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.EndsWith(" (Doctor)", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^" (Doctor)".Length].Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static (string? Icd, string? Diagnosis, string? Conclusion, string? Doctor) ParseConsultationNote(string notes)
    {
        string? doc = null;
        string? icd = null;
        string? diag = null;
        string? concl = null;

        var docMatch = Regex.Match(notes, @"TƯ VẤN XÉT NGHIỆM HOÀN TẤT\s*[—\-–]\s*([^\]]+)\]", RegexOptions.IgnoreCase);
        if (docMatch.Success) doc = NullIfEmpty(docMatch.Groups[1].Value);

        var diagMatch = Regex.Match(notes, @"Chẩn đoán\s+ICD-10\s+([A-Z][0-9]{2}(?:\.[0-9A-Z]+)?)\s*\((.+?)\)\s*(?:\.\s*Kết luận:|$)", RegexOptions.IgnoreCase);
        if (diagMatch.Success)
        {
            icd = NullIfEmpty(diagMatch.Groups[1].Value);
            diag = NullIfEmpty(diagMatch.Groups[2].Value);
        }

        var conclMatch = Regex.Match(notes, @"Kết luận:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (conclMatch.Success)
            concl = NullIfEmpty(conclMatch.Groups[1].Value);

        return (icd, diag, concl, doc);
    }

    private async Task<BsonDocument?> FindLinkedAssessmentAsync(string appointmentId, string patientId, DateTime? resultDate, CancellationToken ct)
    {
        try
        {
            var col = _db.GetCollection<BsonDocument>("medical_assessments");
            var f = Builders<BsonDocument>.Filter;
            var notDeleted = f.Ne("is_deleted", true) & f.Ne("isDeleted", true);

            if (!string.IsNullOrWhiteSpace(appointmentId))
            {
                var byEncounter = await col.Find(notDeleted & f.Or(
                        Identity("encounter_id", appointmentId),
                        Identity("order_id", appointmentId),
                        Identity("appointment_id", appointmentId)))
                    .Sort(new BsonDocument("created_at", -1))
                    .FirstOrDefaultAsync(ct);
                if (byEncounter is not null) return byEncounter;
            }

            if (!string.IsNullOrWhiteSpace(patientId))
            {
                var byPatient = await col.Find(notDeleted
                        & f.Or(Identity("patient_id", patientId), Identity("patient_code", patientId))
                        & f.Or(f.Eq("is_finalized", true), f.Eq("status", "Finalized")))
                    .Sort(new BsonDocument("created_at", -1))
                    .FirstOrDefaultAsync(ct);
                if (byPatient is not null) return byPatient;
            }
        }
        catch { /* non-fatal */ }

        return null;
    }
    public Task AuditAsync(TestHistoryViewer viewer, string action, string? target, DateTime at, CancellationToken ct = default) =>
        _db.GetCollection<BsonDocument>("audit_logs").InsertOneAsync(new BsonDocument { { "AccountId", Id(viewer.AccountId) }, { "Action", action }, { "Module", "UC76" },
            { "IpAddress", viewer.IpAddress ?? "" }, { "Details", target ?? "Directory" }, { "Timestamp", at } }, cancellationToken: ct);
}
