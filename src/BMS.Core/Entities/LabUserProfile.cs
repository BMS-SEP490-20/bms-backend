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
}

/// <summary>Minimal reference data from the existing lab_rooms collection.</summary>
public sealed record LabProfileRoom(string Id, string Code, string Name);
