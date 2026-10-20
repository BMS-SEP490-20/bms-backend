using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Clinical laboratory test note entity attached to a specimen or test order (UC52 / Add Test Notes).
/// Records technical observations, specimen conditions, instrument alerts, and quality concerns (ISO 15189).
/// Maps to the <c>test_notes</c> collection in MongoDB.
/// </summary>
[BsonIgnoreExtraElements]
public class TestNote
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("specimen_id")]
    public string SpecimenId { get; set; } = string.Empty;

    [BsonElement("sample_id")]
    [BsonIgnoreIfNull]
    public string? SampleId { get; set; }

    [BsonElement("sample_barcode")]
    public string SampleBarcode { get; set; } = string.Empty;

    [BsonElement("patient_id")]
    [BsonIgnoreIfNull]
    public string? PatientId { get; set; }

    [BsonElement("patient_name")]
    [BsonIgnoreIfNull]
    public string? PatientName { get; set; }

    [BsonElement("test_package_name")]
    [BsonIgnoreIfNull]
    public string? TestPackageName { get; set; }

    [BsonElement("note_type")]
    public string NoteType { get; set; } = "Technical observation";

    [BsonElement("visibility")]
    public string Visibility { get; set; } = "Internal"; // "Internal" or "Report"

    [BsonElement("is_internal_only")]
    public bool IsInternalOnly { get; set; } = true;

    [BsonElement("print_on_report")]
    public bool PrintOnReport { get; set; } = false;

    [BsonElement("tags")]
    public List<string> Tags { get; set; } = new();

    [BsonElement("content")]
    public string Content { get; set; } = string.Empty;

    [BsonElement("created_by_user_id")]
    [BsonIgnoreIfNull]
    public string? CreatedByUserId { get; set; }

    [BsonElement("created_by_user_name")]
    [BsonIgnoreIfNull]
    public string? CreatedByUserName { get; set; }

    [BsonElement("created_by_user_role")]
    [BsonIgnoreIfNull]
    public string? CreatedByUserRole { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updated_at")]
    [BsonIgnoreIfNull]
    public DateTime? UpdatedAt { get; set; }
}
