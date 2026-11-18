using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Domain entity representing a biological reference interval (UC57 / BR-LAB-02 / BR-LAB-03).
/// Maps to MongoDB collection "reference_intervals".
/// Conforms to: ISO 15189:2022, Decision 2429/QD-BYT, ISO 27789:2021 and WHO/ADA standards.
/// </summary>
[BsonIgnoreExtraElements]
public class ReferenceIntervalEntity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("criterionId")]
    public string CriterionId { get; set; } = string.Empty;

    [BsonElement("criterionCode")]
    public string CriterionCode { get; set; } = string.Empty;

    /// <summary>
    /// Alias for CriterionCode / TestId (UC57).
    /// </summary>
    [BsonIgnore]
    public string TestId
    {
        get => !string.IsNullOrWhiteSpace(CriterionCode) ? CriterionCode : CriterionId;
        set
        {
            CriterionCode = value;
            if (string.IsNullOrWhiteSpace(CriterionId)) CriterionId = value;
        }
    }

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

    [BsonIgnore]
    public double? LowerLimit
    {
        get => NormalMin;
        set => NormalMin = value;
    }

    [BsonIgnore]
    public double? UpperLimit
    {
        get => NormalMax;
        set => NormalMax = value;
    }

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

    /// <summary>
    /// Date when this reference interval configuration takes effect (ISO 15189:2022).
    /// </summary>
    [BsonElement("effectiveFrom")]
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Expiration / end date of this interval configuration if replaced or phased out.
    /// </summary>
    [BsonElement("effectiveTo")]
    public DateTime? EffectiveTo { get; set; }

    /// <summary>
    /// Status: "ACTIVE", "INACTIVE", "DRAFT", "RETIRED".
    /// </summary>
    [BsonElement("status")]
    public string Status { get; set; } = "ACTIVE";

    [BsonElement("createdBy")]
    public string? CreatedBy { get; set; }

    [BsonElement("approvedBy")]
    public string? ApprovedBy { get; set; }

    /// <summary>
    /// Version counter for ISO 15189 configuration change audit.
    /// </summary>
    [BsonElement("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// Snapshot history of prior versions before updates (ISO 15189 / ISO 27789).
    /// </summary>
    [BsonElement("previousVersions")]
    public List<ReferenceIntervalVersionSnapshot> PreviousVersions { get; set; } = new();

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

/// <summary>
/// Historical version snapshot of a Reference Interval (ISO 15189 / ISO 27789).
/// </summary>
[BsonIgnoreExtraElements]
public class ReferenceIntervalVersionSnapshot
{
    [BsonElement("version")]
    public int Version { get; set; }

    [BsonElement("lowerLimit")]
    public double? LowerLimit { get; set; }

    [BsonElement("upperLimit")]
    public double? UpperLimit { get; set; }

    [BsonElement("unit")]
    public string Unit { get; set; } = string.Empty;

    [BsonElement("gender")]
    public string Gender { get; set; } = "All";

    [BsonElement("ageMin")]
    public double AgeMin { get; set; }

    [BsonElement("ageMax")]
    public double AgeMax { get; set; }

    [BsonElement("effectiveFrom")]
    public DateTime EffectiveFrom { get; set; }

    [BsonElement("effectiveTo")]
    public DateTime? EffectiveTo { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "ACTIVE";

    [BsonElement("changedBy")]
    public string? ChangedBy { get; set; }

    [BsonElement("changedAt")]
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("changeReason")]
    public string? ChangeReason { get; set; }
}
