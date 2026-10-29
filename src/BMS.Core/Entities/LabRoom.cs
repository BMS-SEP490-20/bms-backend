using System;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Laboratory room managed by UC "Manage Lab Rooms" (#43).
/// Mapped onto the existing <c>lab_rooms</c> collection (snake_case fields) that
/// UC61 Lab Profiles and Shift management already read, so all features share one source of truth.
/// EF navigation collections from the legacy microservice were removed: MongoDB has no lazy joins,
/// and assignments/profiles are queried through their own repositories.
/// </summary>
[BsonIgnoreExtraElements]
public class LabRoom
{
    /// <summary>Room identifier (e.g. <c>R001</c>); legacy ObjectId ids are read as strings.</summary>
    [BsonId]
    [BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
    public string RoomId { get; set; } = null!;

    /// <summary>Display name of the room.</summary>
    [BsonElement("room_name")]
    public string RoomName { get; set; } = null!;

    /// <summary>Physical location (floor / building).</summary>
    [BsonElement("room_location")]
    [BsonIgnoreIfNull]
    public string? RoomLocation { get; set; }

    /// <summary>Room type, e.g. Hematology, Biochemistry.</summary>
    [BsonElement("room_type")]
    [BsonIgnoreIfNull]
    public string? RoomType { get; set; }

    /// <summary>Whether the room is currently available for assignment.</summary>
    [BsonElement("is_available")]
    [BsonIgnoreIfNull]
    public bool? IsAvailable { get; set; }

    /// <summary>Free-text list of equipment assigned to the room.</summary>
    [BsonElement("assigned_equipment")]
    [BsonIgnoreIfNull]
    public string? AssignedEquipment { get; set; }

    /// <summary>Account id of the technician in charge.</summary>
    [BsonElement("technician_in_charge")]
    [BsonIgnoreIfNull]
    public string? TechnicianInCharge { get; set; }

    /// <summary>UTC creation time.</summary>
    [BsonElement("created_at")]
    [BsonIgnoreIfNull]
    public DateTime? CreatedAt { get; set; }

    /// <summary>Soft-delete flag; other modules already filter on <c>is_deleted</c>.</summary>
    [BsonElement("is_deleted")]
    public bool IsDeleted { get; set; }
}
