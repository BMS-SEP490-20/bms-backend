using System.Text.RegularExpressions;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>Projects only tracking evidence from the existing Mongo collections, preserving legacy schema.</summary>
public sealed class SampleTimelineRepository : ISampleTimelineRepository
{
    private readonly IMongoDatabase _db;
    private IMongoCollection<BsonDocument> Samples => _db.GetCollection<BsonDocument>("blood_samples");
    public SampleTimelineRepository(IMongoDatabase database)
    {
        _db = database;
        Samples.Indexes.CreateMany([
            new(Builders<BsonDocument>.IndexKeys.Descending("collected_at").Ascending("_id")),
            new(Builders<BsonDocument>.IndexKeys.Ascending("patient_id").Descending("collected_at").Ascending("_id")),
            new(Builders<BsonDocument>.IndexKeys.Ascending("status").Descending("collected_at").Ascending("_id"))
        ]);
        // Preserve the existing unique barcode index; recreating it as nonunique would fail at startup.
        using (var indexes = Samples.Indexes.List())
            if (!indexes.ToList().Any(i => i["key"].AsBsonDocument.Equals(new BsonDocument("barcode", 1))))
                Samples.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("barcode")));
        _db.GetCollection<BsonDocument>("rfid_tracking_logs").Indexes.CreateMany([
            new(Builders<BsonDocument>.IndexKeys.Ascending("sample_id").Ascending("scanned_at").Ascending("_id")),
            new(Builders<BsonDocument>.IndexKeys.Ascending("rfid_tag").Ascending("scanned_at").Ascending("_id"))
        ]);
    }
    private static FilterDefinition<BsonDocument> Identity(string field, string id) => ObjectId.TryParse(id, out var oid)
        ? new BsonDocument(field, new BsonDocument("$in", new BsonArray { id, oid })) : new BsonDocument(field, id);
    private static FilterDefinition<BsonDocument> Active => new BsonDocument { { "is_deleted", new BsonDocument("$ne", true) }, { "isDeleted", new BsonDocument("$ne", true) }, { "isActive", new BsonDocument("$ne", false) } };
    private static string Text(BsonDocument d, string key) => d.TryGetValue(key, out var v) && !v.IsBsonNull ? v.ToString()! : "";
    private static DateTime? Date(BsonDocument d, string key) => d.TryGetValue(key, out var v) && v.IsBsonDateTime ? v.ToUniversalTime() : null;
    private static bool Signature(BsonDocument d, string key) => d.TryGetValue(key, out var v) && v.IsString && !string.IsNullOrWhiteSpace(v.AsString);
    private static IEnumerable<BsonDocument> Documents(BsonDocument d, string key) => d.TryGetValue(key, out var v) && v.IsBsonArray
        ? v.AsBsonArray.Where(x => x.IsBsonDocument).Select(x => x.AsBsonDocument) : [];
    private static SampleTimelineSummary Summary(BsonDocument d) => new(Text(d, "_id"), Text(d, "barcode"), Text(d, "patient_id"), Text(d, "status"), Text(d, "rfid_tag"), Date(d, "collected_at"));
    private static FilterDefinition<BsonDocument> Scope(List<string>? ids) => ids is null ? Active : Active & Builders<BsonDocument>.Filter.In("patient_id",
        ids.SelectMany(id => ObjectId.TryParse(id, out var oid) ? new BsonValue[] { id, oid } : new BsonValue[] { id }));
    private static readonly BsonDocument SummaryProjection = new() { { "_id", 1 }, { "barcode", 1 }, { "patient_id", 1 }, { "status", 1 }, { "rfid_tag", 1 }, { "collected_at", 1 } };

    public async Task<List<string>> OwnPatientIdsAsync(string accountId, CancellationToken ct = default)
    {
        // Only an explicit account link grants access. Unverified family relations are not an authorization source.
        var docs = await _db.GetCollection<BsonDocument>("patients").Find(Active & Identity("AccountId", accountId))
            .Project(new BsonDocument("_id", 1)).ToListAsync(ct);
        return docs.Select(x => Text(x, "_id")).Where(x => x.Length > 0).ToList();
    }
    public async Task<(List<SampleTimelineSummary> Items, long Total)> ListAsync(SampleTimelineSearch search, List<string>? patientIds, CancellationToken ct = default)
    {
        var f = Builders<BsonDocument>.Filter;
        var filter = Scope(patientIds);
        if (!string.IsNullOrEmpty(search.Search))
        {
            var regex = new BsonRegularExpression(Regex.Escape(search.Search), "i");
            filter &= f.Or(Identity("_id", search.Search), f.Regex("barcode", regex), Identity("patient_id", search.Search));
        }
        if (!string.IsNullOrEmpty(search.Status)) filter &= f.Eq("status", search.Status);
        if (search.FromDate.HasValue) filter &= f.Gte("collected_at", DateTime.SpecifyKind(search.FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc));
        if (search.ToDate.HasValue) filter &= f.Lt("collected_at", DateTime.SpecifyKind(search.ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc));
        var count = await Samples.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await Samples.Find(filter).Sort(new BsonDocument { { "collected_at", -1 }, { "_id", 1 } })
            .Skip((search.Page - 1) * search.PageSize).Limit(search.PageSize).Project(SummaryProjection).ToListAsync(ct);
        return (docs.Select(Summary).ToList(), count);
    }
    public async Task<SampleTimelineRecord?> GetAsync(string code, List<string>? patientIds, CancellationToken ct = default)
    {
        var f = Builders<BsonDocument>.Filter;
        var projection = new BsonDocument(SummaryProjection) { { "status_history", 1 }, { "created_at", 1 }, { "in_transit_at", 1 },
            { "received_at", 1 }, { "processed_at", 1 }, { "completed_at", 1 }, { "rejected_at", 1 }, { "appointment_id", 1 } };
        // IDs and barcodes are exact. A shared RFID tag must never silently select an arbitrary patient's sample.
        var exactId = await Samples.Find(Scope(patientIds) & Identity("_id", code)).Project(projection).FirstOrDefaultAsync(ct);
        var d = exactId is null ? await Samples.Find(Scope(patientIds) & f.Eq("barcode", code)).Project(projection).Limit(2).ToListAsync(ct) : [exactId];
        if (d.Count == 0) return null;
        if (d.Count > 1) throw new BMS.Core.Exceptions.DomainValidationException("AMBIGUOUS_SAMPLE_CODE", "Mã tra cứu trùng nhiều mẫu. Hãy dùng ID mẫu chính xác.");
        var sample = d[0]; var summary = Summary(sample); var events = new List<SampleTimelineEvent>();
        var sequence = 0;
        foreach (var h in Documents(sample, "status_history"))
        {
            var status = Text(h, "to_status");
            events.Add(new($"history:{sequence++}", status, status, Date(h, "changed_at"), "blood_samples.status_history", Text(h, "operator_name"), null, Text(h, "notes")));
        }
        foreach (var (field, status) in new[] { ("created_at", "SampleCreated"), ("collected_at", "Collected"), ("in_transit_at", "InTransit"),
            ("received_at", "Received"), ("processed_at", "Processing"), ("completed_at", "Completed"), ("rejected_at", "Rejected") })
        {
            var at = Date(sample, field);
            if (at.HasValue && !events.Any(e => e.Code.Equals(status, StringComparison.OrdinalIgnoreCase) && e.Timestamp == at))
                events.Add(new("sample:" + field, status, status, at, "blood_samples." + field));
        }
        var appointment = Text(sample, "appointment_id");
        if (appointment.Length > 0)
        {
            var a = await _db.GetCollection<BsonDocument>("appointments").Find(Active & Identity("_id", appointment) & Identity("patient_id", summary.PatientId))
                .Project(new BsonDocument { { "_id", 0 }, { "check_in_time", 1 } }).FirstOrDefaultAsync(ct);
            if (a is not null && Date(a, "check_in_time") is DateTime checkIn)
                events.Add(new("appointment:check_in_time", "CheckedIn", "CheckedIn", checkIn, "appointments.check_in_time"));
        }
        var specimens = string.IsNullOrWhiteSpace(summary.Barcode) || string.IsNullOrWhiteSpace(summary.PatientId) ? [] :
            await _db.GetCollection<BsonDocument>("specimen_histories").Find(Active & f.Eq("barcode", summary.Barcode) & Identity("patientId", summary.PatientId))
                .Project(new BsonDocument { { "_id", 1 }, { "timeline", 1 } }).ToListAsync(ct);
        foreach (var specimen in specimens)
        {
            sequence = 0;
            foreach (var e in Documents(specimen, "timeline"))
                events.Add(new($"specimen:{Text(specimen, "_id")}:{sequence++}", Text(e, "eventType"), Text(e, "title"), Date(e, "timestamp"),
                    "specimen_histories.timeline", Text(e, "operatorName"), Text(e, "location"), Text(e, "notes")));
        }
        var reports = await _db.GetCollection<BsonDocument>("test_results").Find(Active &
            (Identity("sample_id", summary.Id) | Identity("sampleId", summary.Id)) &
            (Identity("patient_id", summary.PatientId) | Identity("patientId", summary.PatientId)))
            .Project(new BsonDocument { { "_id", 0 }, { "status", 1 }, { "validationStatus", 1 }, { "digital_signature", 1 }, { "digitalSignature", 1 },
                { "entered_at", 1 }, { "enteredAt", 1 }, { "approved_at", 1 }, { "approvedAt", 1 }, { "rerun_requested", 1 }, { "rerunRequested", 1 } }).ToListAsync(ct);
        var results = reports.Select(r => new SampleTimelineResult(
            (Text(r, "status").Equals("APPROVED", StringComparison.OrdinalIgnoreCase) || Text(r, "validationStatus") is "DoctorApproved" or "3") &&
            (Signature(r, "digital_signature") || Signature(r, "digitalSignature")) &&
            !r.GetValue("rerun_requested", false).Equals(BsonBoolean.True) && !r.GetValue("rerunRequested", false).Equals(BsonBoolean.True),
            Date(r, "entered_at") ?? Date(r, "enteredAt"), Date(r, "approved_at") ?? Date(r, "approvedAt"))).ToList();
        return new(summary, events, results);
    }
    public async Task<(List<SampleTimelineRfid> Items, long Total)> RfidAsync(SampleTimelineSummary sample, int page, int pageSize, CancellationToken ct = default)
    {
        var f = Builders<BsonDocument>.Filter;
        var direct = Identity("sample_id", sample.Id);
        var filter = direct;
        if (!string.IsNullOrWhiteSpace(sample.RfidTag) && sample.CollectedAt.HasValue)
            filter |= f.Eq("rfid_tag", sample.RfidTag) & f.Exists("sample_id", false) & f.Gte("scanned_at", sample.CollectedAt.Value);
        var collection = _db.GetCollection<BsonDocument>("rfid_tracking_logs");
        filter &= Active;
        var total = await collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await collection.Find(filter).Sort(new BsonDocument { { "scanned_at", 1 }, { "_id", 1 } }).Skip((page - 1) * pageSize).Limit(pageSize)
            .Project(new BsonDocument { { "_id", 1 }, { "sample_id", 1 }, { "scanned_at", 1 }, { "checkpoint_name", 1 }, { "location", 1 },
                { "reader_device_id", 1 }, { "scanner_id", 1 }, { "operator_name", 1 }, { "status", 1 } }).ToListAsync(ct);
        return (docs.Select(d => new SampleTimelineRfid(Text(d, "_id"), Date(d, "scanned_at"),
            Text(d, "checkpoint_name") is { Length: > 0 } checkpoint ? checkpoint : Text(d, "location"),
            Text(d, "reader_device_id") is { Length: > 0 } reader ? reader : Text(d, "scanner_id"), Text(d, "operator_name"),
            Text(d, "sample_id").Length > 0 ? "Sample" : "Tag", Text(d, "status"))).ToList(), total);
    }
}
