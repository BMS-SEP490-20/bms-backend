using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// One BHYT beneficiary group code (the 2 letters at the start of a card) in the configurable catalog
/// <c>bhyt_group_codes</c> (Quy trình ruling Đợt 3A Q1; open point C2 = the full official list).
/// </summary>
/// <remarks>WHY a catalog instead of a constant list: the official list changes (e.g. NQ 261/2025 added LH) and the
/// clinic must be able to follow it without a release. Admin or Manager edits it; every change is audited.</remarks>
[BsonIgnoreExtraElements]
public class BhytGroupCode
{
    /// <summary>Two upper-case letters, e.g. DN. Also the document id (one row per code).</summary>
    [BsonId]
    public string Code { get; set; } = string.Empty;

    /// <summary>Free-text description entered by Admin/Manager (seed rows have none: no source text was given).</summary>
    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Only active codes are accepted on a card.</summary>
    [BsonElement("is_active")]
    public bool IsActive { get; set; } = true;

    /// <summary>True for rows inserted by the startup seed.</summary>
    [BsonElement("seeded")]
    public bool Seeded { get; set; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    [BsonIgnoreIfNull]
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Fee parameters set by the Manager (single document <c>billing_settings/fee-policy</c>).
/// </summary>
/// <remarks>Quy trình ruling Đợt 3A Q2: the priority-patient discount defaults to 0 VND. "Priority" in the rules
/// (age ≥ 80, pregnant ≥ 7 months, infant &lt; 12 months) is queue priority, not a price cut, so no rate is assumed.</remarks>
[BsonIgnoreExtraElements]
public class FeePolicySettings
{
    /// <summary>Fixed id of the single settings document.</summary>
    public const string DocumentId = "fee-policy";

    [BsonId]
    public string Id { get; set; } = DocumentId;

    /// <summary>Percent (0–100) of the remaining test fee after BHYT; 0 = no discount (default).</summary>
    [BsonElement("priority_discount_percent")]
    public decimal PriorityDiscountPercent { get; set; }

    /// <summary>Upper bound of the discount in VND, or null for no cap.</summary>
    [BsonElement("priority_discount_max_amount")]
    [BsonIgnoreIfNull]
    public decimal? PriorityDiscountMaxAmount { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    [BsonIgnoreIfNull]
    public string? UpdatedBy { get; set; }
}
