using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB implementation of <see cref="ILabRoomRepository"/> over <c>lab_rooms</c>.
/// Replaces the legacy EF Core implementation (TestOrderManagementDbContext does not exist in BMS).
/// </summary>
public sealed class LabRoomRepository : ILabRoomRepository
{
    private const string RoomIdPrefix = "R";
    private readonly IMongoCollection<LabRoom> _rooms;

    /// <summary>Creates the repository bound to <c>lab_rooms</c>.</summary>
    public LabRoomRepository(IMongoDatabase database)
    {
        _rooms = database.GetCollection<LabRoom>("lab_rooms");
    }

    private static FilterDefinition<LabRoom> Active => Builders<LabRoom>.Filter.Ne(r => r.IsDeleted, true);
    private static FilterDefinition<LabRoom> ById(string id) => Builders<LabRoom>.Filter.Eq(r => r.RoomId, id) & Active;

    /// <inheritdoc />
    public async Task<(IReadOnlyList<LabRoom> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var total = await _rooms.CountDocumentsAsync(Active, cancellationToken: ct);
        var items = await _rooms.Find(Active).SortBy(r => r.RoomId)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);
        return (items, total);
    }

    /// <inheritdoc />
    public async Task<LabRoom?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _rooms.Find(ById(id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<int> GetMaxRoomSequenceAsync(CancellationToken ct = default)
    {
        // Ids are zero-padded (R001), so lexical order equals numeric order within the same width.
        var filter = Builders<LabRoom>.Filter.Regex(r => r.RoomId, new BsonRegularExpression($"^{RoomIdPrefix}\\d+$"));
        var last = await _rooms.Find(filter).SortByDescending(r => r.RoomId).Limit(1)
            .Project(r => r.RoomId).FirstOrDefaultAsync(ct);
        return last is not null && int.TryParse(last[RoomIdPrefix.Length..], out var n) ? n : 0;
    }

    /// <inheritdoc />
    public async Task<bool> TryAddAsync(LabRoom room, CancellationToken ct = default)
    {
        try
        {
            await _rooms.InsertOneAsync(room, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(LabRoom room, CancellationToken ct = default)
    {
        var u = Builders<LabRoom>.Update;
        var result = await _rooms.UpdateOneAsync(ById(room.RoomId), u
            .Set(r => r.RoomName, room.RoomName)
            .Set(r => r.RoomLocation, room.RoomLocation)
            .Set(r => r.RoomType, room.RoomType)
            .Set(r => r.IsAvailable, room.IsAvailable)
            .Set(r => r.AssignedEquipment, room.AssignedEquipment)
            .Set(r => r.TechnicianInCharge, room.TechnicianInCharge), cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    /// <inheritdoc />
    public async Task<bool> SoftDeleteAsync(string id, CancellationToken ct = default)
    {
        var result = await _rooms.UpdateOneAsync(ById(id), Builders<LabRoom>.Update.Set(r => r.IsDeleted, true), cancellationToken: ct);
        return result.MatchedCount > 0;
    }
}
