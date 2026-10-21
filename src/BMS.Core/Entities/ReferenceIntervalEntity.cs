using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Domain entity representing a biological reference interval (UC57 / BR-LAB-02 / BR-LAB-03).
/// Maps to MongoDB collection "reference_intervals".
/// Conforms to: ISO 15189:2022, Decision 2429/QD-BYT and WHO/ADA standards.
/// </summary>
public class ReferenceIntervalEntity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("criterionId")]
    public string CriterionId { get; set; } = string.Empty;

    [BsonElement("criterionCode")]
    public string CriterionCode { get; set; } = string.Empty;

    [BsonElement("criterionName")]
    public string CriterionName { get; set; } = string.Empty;

    [BsonElement("unit")]
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// Target gender: "All" (Tất cả), "Male" (Nam), "Female" (Nữ).
    /// </summary>
    [BsonElement("gender")]
    public string Gender { get; set; } = "All";

    /// <summary>
    /// Minimum age in AgeUnit (inclusive).
    /// </summary>
    [BsonElement("ageMin")]
    public double AgeMin { get; set; } = 0;

    /// <summary>
    /// Maximum age in AgeUnit (inclusive).
    /// </summary>
    [BsonElement("ageMax")]
    public double AgeMax { get; set; } = 120;

    /// <summary>
    /// Unit of age: "Years", "Months", "Days". Default is "Years".
    /// </summary>
    [BsonElement("ageUnit")]
    public string AgeUnit { get; set; } = "Years";

    /// <summary>
    /// Human-readable demographic description (e.g., "Nam trưởng thành 18-65 tuổi", "Trẻ sơ sinh").
    /// </summary>
    [BsonElement("demographicLabel")]
    public string DemographicLabel { get; set; } = string.Empty;

    /// <summary>
    /// Lower biological normal limit.
    /// </summary>
    [BsonElement("normalMin")]
    public double? NormalMin { get; set; }

    /// <summary>
    /// Upper biological normal limit.
    /// </summary>
    [BsonElement("normalMax")]
    public double? NormalMax { get; set; }

    /// <summary>
    /// Text value for qualitative / semi-quantitative tests (e.g. "Âm tính (Negative)").
    /// </summary>
    [BsonElement("normalText")]
    public string? NormalText { get; set; }

    /// <summary>
    /// Critical / Panic Low threshold (BR-LAB-03). Below this value indicates life-threatening risk.
    /// </summary>
    [BsonElement("panicLow")]
    public double? PanicLow { get; set; }

    /// <summary>
    /// Critical / Panic High threshold (BR-LAB-03). Above this value indicates life-threatening risk.
    /// </summary>
    [BsonElement("panicHigh")]
    public double? PanicHigh { get; set; }

    /// <summary>
    /// Clinical condition / context (e.g. "General", "Fasting", "Pregnancy").
    /// </summary>
    [BsonElement("condition")]
    public string Condition { get; set; } = "General";

    /// <summary>
    /// Reference source authority (e.g. "Quyết định 2429/QĐ-BYT", "WHO Laboratory Manual", "ADA 2024").
    /// </summary>
    [BsonElement("standardAuthority")]
    public string StandardAuthority { get; set; } = "Quyết định 2429/QĐ-BYT";

    /// <summary>
    /// Clinical interpretation or notes.
    /// </summary>
    [BsonElement("notes")]
    public string Notes { get; set; } = string.Empty;

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Soft-delete flag preserving historical clinical laboratory data.
    /// </summary>
    [BsonElement("isDeleted")]
    public bool IsDeleted { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
