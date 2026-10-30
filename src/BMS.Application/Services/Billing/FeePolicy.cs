namespace BMS.Application.Services.Billing;

/// <summary>
/// Configurable inputs of the fee formula: active BHYT group codes (Admin/Manager catalog) and the Manager's
/// priority-discount parameters. Loaded per calculation by <see cref="Interfaces.IFeePolicyProvider"/>.
/// </summary>
/// <param name="BhytGroupCodes">Active 2-letter group codes, upper-case.</param>
/// <param name="PriorityDiscountPercent">0–100; 0 = no discount (default, Quy trình Đợt 3A Q2).</param>
/// <param name="PriorityDiscountMaxAmount">Cap in VND, or null for none.</param>
public sealed record FeePolicy(IReadOnlySet<string> BhytGroupCodes, decimal PriorityDiscountPercent = 0m, decimal? PriorityDiscountMaxAmount = null)
{
    /// <summary>Builds a policy from a code list (codes are normalized: trim + upper-case).</summary>
    public static FeePolicy From(IEnumerable<string> codes, decimal percent = 0m, decimal? max = null) =>
        new(new HashSet<string>(codes.Select(BhytCard.NormalizeGroupCode).Where(c => c.Length > 0), StringComparer.Ordinal), percent, max);

    /// <summary>The state of a fresh database: the 8 seeded group codes and a 0 VND priority discount.</summary>
    public static FeePolicy SeedDefault { get; } = From(BhytCard.SeedGroupCodes);
}
