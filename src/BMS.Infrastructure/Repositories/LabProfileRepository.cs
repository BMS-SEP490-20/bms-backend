using System.Text.RegularExpressions;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>Reads the existing Lab profile and room collections without simulated fallback data.</summary>
public sealed class LabProfileRepository : ILabProfileRepository
{
    private readonly IMongoCollection<LabUserProfile> _profiles;
    private readonly IMongoCollection<BsonDocument> _rooms;
    private readonly IMongoCollection<BsonDocument> _accounts;

    public LabProfileRepository(IMongoDatabase database)
    {
        _profiles = database.GetCollection<LabUserProfile>("lab_user_profiles");
        _rooms = database.GetCollection<BsonDocument>("lab_rooms");
        _accounts = database.GetCollection<BsonDocument>("accounts");
        _profiles.Indexes.CreateMany([
            new(Builders<LabUserProfile>.IndexKeys.Ascending(x => x.AssignedRoom).Ascending(x => x.FullName).Ascending(x => x.Id)),
            new(Builders<LabUserProfile>.IndexKeys.Ascending(x => x.CreatedAt).Ascending(x => x.Id))
        ]);
    }

    private static BsonDocument Identity(string field, string id) => ObjectId.TryParse(id, out var oid)
        ? new(field, new BsonDocument("$in", new BsonArray { id, oid })) : new(field, id);
    private static FilterDefinition<LabUserProfile> ProfileId(string id) => new BsonDocumentFilterDefinition<LabUserProfile>(Identity("_id", id));

    public async Task<(List<LabUserProfile> Items, long Total)> ListAsync(LabProfileSearch search, CancellationToken ct = default)
    {
        var f = Builders<LabUserProfile>.Filter;
        var filter = f.Ne(x => x.IsDeleted, true);
        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var regex = new BsonRegularExpression(Regex.Escape(search.Search), "i");
            filter &= f.Or(f.Regex(x => x.FullName, regex), f.Regex(x => x.Qualification, regex), ProfileId(search.Search));
        }
        if (!string.IsNullOrWhiteSpace(search.RoomId)) filter &= f.Eq(x => x.AssignedRoom, search.RoomId);
        // Filter creation timestamps by the clinic's UTC+7 calendar days, including both endpoints.
        if (search.CreatedFrom.HasValue) filter &= f.Gte(x => x.CreatedAt, DateTime.SpecifyKind(search.CreatedFrom.Value.Date.AddHours(-7), DateTimeKind.Utc));
        if (search.CreatedTo.HasValue) filter &= f.Lt(x => x.CreatedAt, DateTime.SpecifyKind(search.CreatedTo.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc));
        var total = await _profiles.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _profiles.Find(filter).SortBy(x => x.FullName).ThenBy(x => x.Id)
            .Skip((search.Page - 1) * search.PageSize).Limit(search.PageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<LabUserProfile?> GetAsync(string id, CancellationToken ct = default) =>
        _profiles.Find(ProfileId(id) & Builders<LabUserProfile>.Filter.Ne(x => x.IsDeleted, true)).FirstOrDefaultAsync(ct)!;

    public async Task<List<LabProfileRoom>> RoomsAsync(CancellationToken ct = default)
    {
        var f = Builders<BsonDocument>.Filter;
        var docs = await _rooms.Find(f.Ne("is_deleted", true) & f.Ne("isDeleted", true) & f.Ne("isActive", false))
            .Sort(new BsonDocument("room_name", 1)).Limit(500).Project(new BsonDocument { { "_id", 1 }, { "room_code", 1 }, { "room_name", 1 } }).ToListAsync(ct);
        return docs.Select(d => new LabProfileRoom(d["_id"].ToString()!, d.GetValue("room_code", "").ToString()!,
            d.GetValue("room_name", "").ToString()!)).ToList();
    }

    // Accounts use the established canonical role IDs: 3 = LabTechnician, 4 = LabHead.
    private static FilterDefinition<BsonDocument> EligibleAccounts()
    {
        var f = Builders<BsonDocument>.Filter;
        return f.In("RoleId", new[] { "3", "4" }) & f.Eq("Status", "Active") &
            f.Ne("IsDeleted", true) & f.Ne("isDeleted", true) & f.Ne("is_deleted", true) &
            f.Ne("IsActive", false) & f.Ne("isActive", false);
    }
    private static readonly BsonDocument AccountProjection = new() { { "_id", 1 }, { "FullName", 1 }, { "Username", 1 } };

    public async Task<(List<LabProfileAccount> Items, long Total)> AccountsAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var f = Builders<BsonDocument>.Filter;
        var filter = EligibleAccounts();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var regex = new BsonRegularExpression(Regex.Escape(search), "i");
            filter &= f.Or(f.Regex("FullName", regex), f.Regex("Username", regex), Identity("_id", search));
        }
        var total = await _accounts.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await _accounts.Find(filter).Sort(new BsonDocument { { "FullName", 1 }, { "_id", 1 } })
            .Skip((page - 1) * pageSize).Limit(pageSize).Project(AccountProjection).ToListAsync(ct);
        var result = new List<LabProfileAccount>();
        foreach (var d in docs) result.Add(await AccountAsync(d, ct));
        return (result, total);
    }
    public async Task<LabProfileAccount?> EligibleAccountAsync(string id, CancellationToken ct = default)
    {
        var doc = await _accounts.Find(EligibleAccounts() & Identity("_id", id)).Project(AccountProjection).FirstOrDefaultAsync(ct);
        return doc is null ? null : await AccountAsync(doc, ct);
    }
    private async Task<LabProfileAccount> AccountAsync(BsonDocument doc, CancellationToken ct)
    {
        var id = doc["_id"].ToString()!;
        return new(id, doc.GetValue("FullName", "").ToString()!, doc.GetValue("Username", "").ToString()!, await HasProfileAsync(id, ct));
    }
    public Task<bool> HasProfileAsync(string id, CancellationToken ct = default) => _profiles.Find(ProfileId(id)).AnyAsync(ct);
    public async Task<bool> InsertAsync(LabUserProfile profile, CancellationToken ct = default)
    {
        try { await _profiles.InsertOneAsync(profile, cancellationToken: ct); return true; }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }
    // Missing revisions are legacy version zero. Active-state and revision checks share the atomic write.
    private static FilterDefinition<LabUserProfile> CurrentVersion(string id, long revision)
    {
        var f = Builders<LabUserProfile>.Filter;
        var version = f.Eq(x => x.Revision, revision);
        if (revision == 0) version |= f.Exists(x => x.Revision, false);
        return ProfileId(id) & f.Ne(x => x.IsDeleted, true) & version;
    }
    public Task<LabUserProfile?> UpdateAsync(string id, long expectedRevision, LabProfileSnapshot details, LabProfileAudit audit, CancellationToken ct = default)
    {
        var u = Builders<LabUserProfile>.Update;
        return _profiles.FindOneAndUpdateAsync(CurrentVersion(id, expectedRevision), u.Set(x => x.FullName, details.FullName)
            .Set(x => x.Qualification, details.Qualification).Set(x => x.AssignedRoom, details.AssignedRoom)
            .Set(x => x.UpdatedAt, audit.At).Inc(x => x.Revision, 1).Push(x => x.Audit, audit),
            new FindOneAndUpdateOptions<LabUserProfile> { ReturnDocument = ReturnDocument.After }, ct)!;
    }
}
