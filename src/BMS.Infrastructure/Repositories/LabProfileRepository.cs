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

    public LabProfileRepository(IMongoDatabase database)
    {
        _profiles = database.GetCollection<LabUserProfile>("lab_user_profiles");
        _rooms = database.GetCollection<BsonDocument>("lab_rooms");
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
}
