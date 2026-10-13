using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Doctor profile linked to an account.
/// Maps to the <c>doctors</c> collection. <c>_id</c> equals the account ObjectId.
/// </summary>
public class Doctor
{
    /// <summary>Same as the linked account ObjectId.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>Full display name (e.g. BS. CKI Nguyễn Văn Minh).</summary>
    [BsonElement("full_name")]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Clinical specialty.</summary>
    [BsonElement("specialty")]
    public string Specialty { get; set; } = string.Empty;

    /// <summary>Medical practice license number.</summary>
    [BsonElement("license_number")]
    public string LicenseNumber { get; set; } = string.Empty;

    /// <summary>Default room assignment (ROOM-xxx).</summary>
    [BsonElement("room_id")]
    public string RoomId { get; set; } = string.Empty;
}