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
        (await _db.GetCollection<BsonDocument>("patients").Find(new BsonDocumentFilterDefinition<BsonDocument>(Active) & Identity("AccountId", accountId))
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
        var status = Text(d, "status"); var validation = Text(d, "validationStatus");
        // Conflicting approval states fail closed; a stored signature alone never publishes clinical values.
        var approved = (status.Length > 0 || validation.Length > 0) && (status.Length == 0 || status.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)) &&
            (validation.Length == 0 || validation is "3" or "DoctorApproved") && signature.IsString && !string.IsNullOrWhiteSpace(signature.AsString) &&
            !d.GetValue("rerun_requested", false).Equals(BsonBoolean.True) && !d.GetValue("rerunRequested", false).Equals(BsonBoolean.True);
        var sampleId = Text(d, "sample_id", "sampleId"); var patientId = Text(d, "patient_id", "patientId");
        var sample = await _db.GetCollection<BsonDocument>("blood_samples").Find(new BsonDocumentFilterDefinition<BsonDocument>(Active) & Identity("_id", sampleId) & Identity("patient_id", patientId))
            .Project(new BsonDocument { { "_id", 0 }, { "status", 1 } }).FirstOrDefaultAsync(ct);
        approved &= sample is not null && new[] { "Completed", "Stored" }.Contains(Text(sample, "status"), StringComparer.OrdinalIgnoreCase);
        var packageId = Text(d, "package_id", "packageId");
        var package = await _db.GetCollection<BsonDocument>("test_packages").Find(Identity("_id", packageId))
            .Project(new BsonDocument { { "_id", 0 }, { "package_name", 1 } }).FirstOrDefaultAsync(ct);
        return new(Text(d, "_id"), Text(d, "result_code", "resultCode"), patientId, sampleId,
            package is null ? packageId : Text(package, "package_name"), Date(d, "created_at", "createdAt"), approved ? Date(d, "approved_at", "approvedAt") : null, approved);
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
        return new(summary, items, Text(d, "conclusion_notes", "clinicalComment"));
    }
    public Task AuditAsync(TestHistoryViewer viewer, string action, string? target, DateTime at, CancellationToken ct = default) =>
        _db.GetCollection<BsonDocument>("audit_logs").InsertOneAsync(new BsonDocument { { "AccountId", Id(viewer.AccountId) }, { "Action", action }, { "Module", "UC76" },
            { "IpAddress", viewer.IpAddress ?? "" }, { "Details", target ?? "Directory" }, { "Timestamp", at } }, cancellationToken: ct);
}
