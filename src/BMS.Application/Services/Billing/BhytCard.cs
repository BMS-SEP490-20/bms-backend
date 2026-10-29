using BMS.Core.Exceptions;

namespace BMS.Application.Services.Billing;

/// <summary>
/// Vietnamese health-insurance (BHYT) card rules (QĐ2, QĐ12, BA:L94, BA-RULES §2/§12).
/// </summary>
/// <remarks>
/// Card = 15 characters: 2 letters (beneficiary group) + 1 digit benefit code + 2 digits province + 10 digits.
/// The benefit always follows the 3rd character (1, 2, 5 → 100%; 3 → 95%; 4 → 80%), never the 2 letters
/// (NQ 261/2025 changed some groups' codes). A card is rejected only for the 4 format errors in QĐ12; an unknown
/// card is never given a default 80%.
/// </remarks>
public static class BhytCard
{
    /// <summary>
    /// Group codes seeded into the configurable catalog <c>bhyt_group_codes</c> on first start (Quy trình ruling
    /// Đợt 3A Q1: the codes present in its source). Admin or Manager adds or deactivates codes afterwards.
    /// </summary>
    /// <remarks>TODO(phat, open point C2): confirm the full official list; until then a card whose group is not in
    /// the catalog is rejected with MSG-ERR-FIN-01 (never priced with a guessed benefit).</remarks>
    public static readonly IReadOnlyList<string> SeedGroupCodes = ["QN", "CA", "CY", "DK", "CN", "HT", "LH", "DN"];

    /// <summary>Normalizes a group code typed by Admin/Manager or read from a card: trim + upper-case (invariant).</summary>
    public static string NormalizeGroupCode(string? raw) => (raw ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Visit BHYT-scope cost below this is fully covered; at or above it the card benefit applies (QĐ2).</summary>
    public const decimal FullCoverageThreshold = 379_500m;

    /// <summary>
    /// Normalizes (trim, upper-case, drop spaces/dashes) BEFORE validating, then validates against the active
    /// group codes of the catalog; returns the card and its benefit %.
    /// </summary>
    /// <param name="raw">Card as typed.</param>
    /// <param name="activeGroupCodes">Active codes from <c>bhyt_group_codes</c> (upper-case).</param>
    /// <exception cref="DomainValidationException">400 MSG-ERR-FIN-01 on any of the 4 format errors.</exception>
    public static (string Card, int BenefitPercent) Parse(string raw, IReadOnlySet<string> activeGroupCodes)
    {
        ArgumentNullException.ThrowIfNull(activeGroupCodes);
        var card = new string((raw ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        if (card.Length != 15) throw Invalid("Mã thẻ BHYT phải gồm đúng 15 ký tự.");
        if (!activeGroupCodes.Contains(card[..2])) throw Invalid("Hai ký tự đầu không thuộc danh mục mã đối tượng BHYT.");
        var benefit = card[2] switch { '1' or '2' or '5' => 100, '3' => 95, '4' => 80, _ => 0 };
        if (benefit == 0) throw Invalid("Ký tự thứ 3 (mã quyền lợi) phải từ 1 đến 5.");
        if (!char.IsAsciiDigit(card[3]) || !char.IsAsciiDigit(card[4])) throw Invalid("Ký tự 4–5 (mã tỉnh) phải là số.");
        return (card, benefit);
    }

    /// <summary>Coverage actually applied: 100% when the BHYT-scope cost is below the threshold, else the card benefit.</summary>
    public static int EffectivePercent(int benefitPercent, decimal bhytScopeCost) =>
        bhytScopeCost < FullCoverageThreshold ? 100 : benefitPercent;

    private static DomainValidationException Invalid(string message) => new("MSG-ERR-FIN-01", message);
}
