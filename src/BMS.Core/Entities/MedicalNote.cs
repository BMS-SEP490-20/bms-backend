using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>An immutable UC51 addendum linked to an existing medical record.</summary>
[BsonIgnoreExtraElements]
public sealed class MedicalNote
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    [BsonElement("recordId")]
    public string RecordId { get; set; } = string.Empty;
    [BsonElement("patientId")]
    public string PatientId { get; set; } = string.Empty;
    [BsonElement("content")]
    public string Content { get; set; } = string.Empty;
    [BsonElement("authorId")]
    public string AuthorId { get; set; } = string.Empty;
    [BsonElement("authorName")]
    public string AuthorName { get; set; } = string.Empty;
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}
