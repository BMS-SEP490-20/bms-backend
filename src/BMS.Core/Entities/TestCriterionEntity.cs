using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Domain entity representing a clinical laboratory test parameter / criterion (UC18).
/// Maps to MongoDB collection "test_criteria".
/// Conforms to: ISO 15189:2022 (Medical Laboratories), Decision 2429/QD-BYT & WHO/ADA reference ranges.
/// </summary>
public class TestCriterionEntity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("criterionCode")]
    public string CriterionCode { get; set; } = string.Empty;

    [BsonElement("criterionName")]
    public string CriterionName { get; set; } = string.Empty;

    [BsonElement("englishName")]
    public string EnglishName { get; set; } = string.Empty;

    [BsonElement("category")]
    public string Category { get; set; } = "Biochemistry";

    [BsonElement("unit")]
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// Type of measurement: "Numeric" (quantitative float) or "Qualitative" (e.g. Negative/Positive).
    /// </summary>
    [BsonElement("valueType")]
    public string ValueType { get; set; } = "Numeric";

    // Biological normal reference range
    [BsonElement("normalMin")]
    public double? NormalMin { get; set; }

    [BsonElement("normalMax")]
    public double? NormalMax { get; set; }

    [BsonElement("normalText")]
    public string? NormalText { get; set; }

    // Panic Critical Values (Red alert thresholds threatening patient life)
    [BsonElement("panicLow")]
    public double? PanicLow { get; set; }

    [BsonElement("panicHigh")]
    public double? PanicHigh { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Invariant: 100% soft-delete only to protect historical medical test records integrity.
    /// </summary>
    [BsonElement("isDeleted")]
    public bool IsDeleted { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
