using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>Existing lab_user_profiles document; its ID is the linked staff account ID.</summary>
[BsonIgnoreExtraElements]
public sealed class LabUserProfile
{
    [BsonId, BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
    public string Id { get; set; } = string.Empty;
    [BsonElement("full_name")]
    public string FullName { get; set; } = string.Empty;
    [BsonElement("qualification")]
    public string Qualification { get; set; } = string.Empty;
    [BsonElement("assigned_room")]
    public string AssignedRoom { get; set; } = string.Empty;
    [BsonElement("revision")]
    public long Revision { get; set; }
    [BsonElement("created_at")]
    public DateTime? CreatedAt { get; set; }
    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }
    [BsonElement("is_deleted")]
    public bool IsDeleted { get; set; }
    [BsonElement("audit")]
    public List<LabProfileAudit> Audit { get; set; } = [];
}

/// <summary>Minimal reference data from the existing lab_rooms collection.</summary>
public sealed record LabProfileRoom(string Id, string Code, string Name);

/// <summary>Safe account projection; credential and contact fields are never loaded.</summary>
public sealed record LabProfileAccount(string Id, string FullName, string Username, bool HasProfile);
public sealed record LabProfileActor(string Id, string Name);
/// <summary>Immutable professional-detail snapshot stored with the mutation.</summary>
public sealed record LabProfileSnapshot(string FullName, string Qualification, string AssignedRoom);
public sealed record LabProfileAudit(string Action, string ActorId, string ActorName, DateTime At,
    string Reason, LabProfileSnapshot? Before, LabProfileSnapshot? After);
