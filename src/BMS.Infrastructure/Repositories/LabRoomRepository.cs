using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB implementation of <see cref="ILabRoomRepository"/> unifying both <c>lab_rooms</c>
/// (laboratory &amp; specimen collection rooms) and <c>doctor_rooms</c> (clinical consultation rooms)
/// so Admin/Manager manage all facility rooms from one directory while keeping Shift &amp; LabProfile collections compatible.
/// </summary>
public sealed class LabRoomRepository : ILabRoomRepository
{
    private const string RoomIdPrefix = "R";
    private readonly IMongoCollection<LabRoom> _rooms;
    private readonly IMongoCollection<BsonDocument> _doctorRooms;
    private readonly IMongoCollection<BsonDocument> _accounts;

    /// <summary>Creates the repository bound to <c>lab_rooms</c>, <c>doctor_rooms</c>, and <c>accounts</c>.</summary>
    public LabRoomRepository(IMongoDatabase database)
    {
        _rooms = database.GetCollection<LabRoom>("lab_rooms");
        _doctorRooms = database.GetCollection<BsonDocument>("doctor_rooms");
        _accounts = database.GetCollection<BsonDocument>("accounts");
    }

    private static FilterDefinition<LabRoom> Active => Builders<LabRoom>.Filter.Ne(r => r.IsDeleted, true);
    private static FilterDefinition<LabRoom> ById(string id) => Builders<LabRoom>.Filter.Eq(r => r.RoomId, id) & Active;

    private static FilterDefinition<BsonDocument> ActiveDoctorRooms =>
        Builders<BsonDocument>.Filter.Ne("is_deleted", true)
        & Builders<BsonDocument>.Filter.Ne("isDeleted", true)
        & Builders<BsonDocument>.Filter.Ne("IsDeleted", true);

    private static bool IsClinicalConsultationRoom(string? roomType)
    {
        if (string.IsNullOrWhiteSpace(roomType)) return false;
        return roomType.Contains("Khám", StringComparison.OrdinalIgnoreCase)
            || roomType.Contains("Lâm sàng", StringComparison.OrdinalIgnoreCase)
            || roomType.Contains("Tư vấn", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadBsonString(BsonDocument doc, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (doc.Contains(k) && !doc[k].IsBsonNull)
            {
                var s = doc[k].ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }

    private static bool ReadBsonBool(BsonDocument doc, bool defaultVal, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (doc.Contains(k) && doc[k].IsBoolean)
                return doc[k].AsBoolean;
        }
        return defaultVal;
    }

    private static LabRoom MapDoctorRoom(BsonDocument doc)
    {
        var id = ReadBsonString(doc, "_id", "room_id") ?? "";
        var name = ReadBsonString(doc, "room_name", "name") ?? id;
        var location = ReadBsonString(doc, "room_location", "floor");
        var roomType = ReadBsonString(doc, "room_type") ?? "Khám lâm sàng";
        var isAvail = ReadBsonBool(doc, true, "is_active", "is_available");
        var equip = ReadBsonString(doc, "assigned_equipment", "equipment");
        var staff = ReadBsonString(doc, "technician_in_charge", "doctor_in_charge");
        DateTime? createdAt = doc.Contains("created_at") && doc["created_at"].IsBsonDateTime
            ? doc["created_at"].ToUniversalTime()
            : null;

        return new LabRoom
        {
            RoomId = id,
            RoomName = name,
            RoomLocation = location,
            RoomType = roomType,
            IsAvailable = isAvail,
            AssignedEquipment = equip,
            TechnicianInCharge = staff,
            CreatedAt = createdAt
        };
    }

    private async Task<Dictionary<string, string>> LoadRoomStaffMapAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var fb = Builders<BsonDocument>.Filter;
            var roleFilter = fb.Or(
                fb.In("roleId", new[] { 2, 3, 6 }),
                fb.In("role_id", new[] { 2, 3, 6 }));
            var activeFilter = fb.Ne("isDeleted", true) & fb.Ne("is_deleted", true) & fb.Ne("isActive", false);
            var staffDocs = await _accounts.Find(roleFilter & activeFilter).ToListAsync(ct);

            foreach (var doc in staffDocs)
            {
                var room = ReadBsonString(doc, "assignedRoom", "assigned_room");
                if (string.IsNullOrWhiteSpace(room)) continue;
                var name = ReadBsonString(doc, "fullName", "full_name", "username");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var code = ReadBsonString(doc, "employeeCode", "employee_code");
                var label = !string.IsNullOrWhiteSpace(code) && !name.Contains(code, StringComparison.OrdinalIgnoreCase)
                    ? $"{name} ({code})"
                    : name;

                if (!map.TryGetValue(room.Trim(), out var list))
                {
                    list = new List<string>();
                    map[room.Trim()] = list;
                }
                if (!list.Contains(label, StringComparer.OrdinalIgnoreCase))
                    list.Add(label);
            }
        }
        catch {  }

        return map.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<LabRoom> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var labItems = await _rooms.Find(Active).ToListAsync(ct);
        var doctorDocs = await _doctorRooms.Find(ActiveDoctorRooms).ToListAsync(ct);
        var doctorItems = doctorDocs.Select(MapDoctorRoom).ToList();
        var roomStaffMap = await LoadRoomStaffMapAsync(ct);

        var labIds = new HashSet<string>(labItems.Select(x => x.RoomId), StringComparer.OrdinalIgnoreCase);
        var combined = new List<LabRoom>(labItems.Count + doctorItems.Count);
        combined.AddRange(doctorItems.Where(d => !labIds.Contains(d.RoomId)));
        combined.AddRange(labItems);

        foreach (var room in combined)
        {
            if (string.IsNullOrWhiteSpace(room.TechnicianInCharge)
                && roomStaffMap.TryGetValue(room.RoomId, out var assignedStaff))
            {
                room.TechnicianInCharge = assignedStaff;
            }
        }

        var sorted = combined.OrderBy(r => r.RoomId, StringComparer.OrdinalIgnoreCase).ToList();
        var total = sorted.Count;
        var paged = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (paged, total);
    }

    /// <inheritdoc />
    public async Task<LabRoom?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var lab = await _rooms.Find(ById(id)).FirstOrDefaultAsync(ct);
        LabRoom? room = lab;
        if (room == null)
        {
            var docFilter = Builders<BsonDocument>.Filter.Eq("_id", id) & ActiveDoctorRooms;
            var doctorDoc = await _doctorRooms.Find(docFilter).FirstOrDefaultAsync(ct);
            room = doctorDoc != null ? MapDoctorRoom(doctorDoc) : null;
        }
        if (room != null && string.IsNullOrWhiteSpace(room.TechnicianInCharge))
        {
            var staffMap = await LoadRoomStaffMapAsync(ct);
            if (staffMap.TryGetValue(room.RoomId, out var assignedStaff))
                room.TechnicianInCharge = assignedStaff;
        }
        return room;
    }

    /// <inheritdoc />
    public async Task<int> GetMaxRoomSequenceAsync(CancellationToken ct = default)
    {
        var filter = Builders<LabRoom>.Filter.Regex(r => r.RoomId, new BsonRegularExpression($"^{RoomIdPrefix}\\d+$"));
        var last = await _rooms.Find(filter).SortByDescending(r => r.RoomId).Limit(1)
            .Project(r => r.RoomId).FirstOrDefaultAsync(ct);
        int maxLab = last is not null && int.TryParse(last[RoomIdPrefix.Length..], out var n) ? n : 0;

        var docFilter = Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression($"^{RoomIdPrefix}\\d+$"));
        var lastDoc = await _doctorRooms.Find(docFilter).Sort(Builders<BsonDocument>.Sort.Descending("_id")).Limit(1).FirstOrDefaultAsync(ct);
        int maxDoc = 0;
        if (lastDoc != null && lastDoc.Contains("_id") && lastDoc["_id"].IsString)
        {
            var idStr = lastDoc["_id"].AsString;
            if (idStr.StartsWith(RoomIdPrefix, StringComparison.Ordinal) && int.TryParse(idStr[RoomIdPrefix.Length..], out var dn))
                maxDoc = dn;
        }

        return Math.Max(maxLab, maxDoc);
    }

    /// <inheritdoc />
    public async Task<bool> TryAddAsync(LabRoom room, CancellationToken ct = default)
    {
        try
        {
            if (IsClinicalConsultationRoom(room.RoomType))
            {
                var doc = new BsonDocument
                {
                    { "_id", room.RoomId },
                    { "room_number", room.RoomId },
                    { "room_name", room.RoomName ?? "" },
                    { "floor", room.RoomLocation ?? "" },
                    { "room_location", room.RoomLocation ?? "" },
                    { "room_type", string.IsNullOrWhiteSpace(room.RoomType) ? "Khám lâm sàng" : room.RoomType },
                    { "is_active", room.IsAvailable ?? true },
                    { "assigned_equipment", room.AssignedEquipment ?? "" },
                    { "technician_in_charge", room.TechnicianInCharge ?? "" },
                    { "is_deleted", false },
                    { "created_at", room.CreatedAt ?? DateTime.UtcNow }
                };
                await _doctorRooms.InsertOneAsync(doc, cancellationToken: ct);
                return true;
            }

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
        if (result.MatchedCount > 0) return true;

        var docFilter = Builders<BsonDocument>.Filter.Eq("_id", room.RoomId) & ActiveDoctorRooms;
        var docUpdate = Builders<BsonDocument>.Update
            .Set("room_name", room.RoomName ?? "")
            .Set("floor", room.RoomLocation ?? "")
            .Set("room_location", room.RoomLocation ?? "")
            .Set("room_type", string.IsNullOrWhiteSpace(room.RoomType) ? "Khám lâm sàng" : room.RoomType)
            .Set("is_active", room.IsAvailable ?? true)
            .Set("assigned_equipment", room.AssignedEquipment ?? "")
            .Set("technician_in_charge", room.TechnicianInCharge ?? "");
        var docResult = await _doctorRooms.UpdateOneAsync(docFilter, docUpdate, cancellationToken: ct);
        return docResult.MatchedCount > 0;
    }

    /// <inheritdoc />
    public async Task<bool> SoftDeleteAsync(string id, CancellationToken ct = default)
    {
        var result = await _rooms.UpdateOneAsync(ById(id), Builders<LabRoom>.Update.Set(r => r.IsDeleted, true), cancellationToken: ct);
        if (result.MatchedCount > 0) return true;

        var docFilter = Builders<BsonDocument>.Filter.Eq("_id", id) & ActiveDoctorRooms;
        var docResult = await _doctorRooms.UpdateOneAsync(docFilter, Builders<BsonDocument>.Update
            .Set("is_deleted", true)
            .Set("is_active", false), cancellationToken: ct);
        return docResult.MatchedCount > 0;
    }
}
