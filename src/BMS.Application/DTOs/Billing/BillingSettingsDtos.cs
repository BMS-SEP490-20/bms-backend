namespace BMS.Application.DTOs.Billing;

/// <summary>One row of the BHYT group-code catalog.</summary>
public sealed class BhytGroupCodeDto
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool Seeded { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Create/update of a BHYT group code (code comes from the route; trimmed and upper-cased by the server).</summary>
public sealed class UpsertBhytGroupCodeRequestDto
{
    /// <summary>Description; null keeps the current one.</summary>
    public string? Description { get; set; }

    /// <summary>Whether cards of this group are accepted.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Priority-discount parameters set by the Manager (default 0 = no discount).</summary>
public sealed class PriorityDiscountSettingsDto
{
    /// <summary>0–100 percent of the remaining test fee after BHYT.</summary>
    public decimal Percent { get; set; }

    /// <summary>Cap in VND, or null for none.</summary>
    public decimal? MaxAmount { get; set; }

    /// <summary>Read only: last change time (UTC) and actor; null when never set.</summary>
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
