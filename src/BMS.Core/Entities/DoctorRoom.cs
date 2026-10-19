using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Examination / consultation room assigned to doctors.
/// Maps to the <c>doctor_rooms</c> collection.
/// </summary>
[BsonIgnoreExtraElements]
public class DoctorRoom
{
    /// <summary>Room identifier (e.g. ROOM-101).</summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    /// <summary>Display room number.</summary>
    [BsonElement("room_number")]
    public string RoomNumber { get; set; } = string.Empty;

    /// <summary>Human-readable room name.</summary>
    [BsonElement("room_name")]
    public string RoomName { get; set; } = string.Empty;

    /// <summary>Floor / zone description.</summary>
    [BsonElement("floor")]
    public string Floor { get; set; } = string.Empty;

    /// <summary>Whether the room is currently usable.</summary>
    [BsonElement("is_active")]
    public bool IsActive { get; set; } = true;
}