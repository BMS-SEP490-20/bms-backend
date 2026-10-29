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
    /// <summary>Beneficiary group codes (QĐ 1351/QĐ-BHXH and later amendments, incl. LH from NQ 261/2025).</summary>
    /// <remarks>TODO(phat, open question Q-FIN-1): confirm the full list with the BHXH catalog in use at the clinic.</remarks>
    public static readonly IReadOnlySet<string> GroupCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "DN", "HX", "CH", "NN", "TK", "HC", "XK", "XB", "XN", "TN", "CS", "QN", "CA", "CY", "MS", "HT", "TB", "NO",
        "CT", "CC", "CK", "CB", "KC", "HD", "BT", "HN", "DT", "DK", "XD", "TS", "TC", "TQ", "TA", "TY", "HG", "LS",
        "PV", "CN", "OK", "HK", "HS", "SV", "GB", "GD", "TE", "TL", "LH", "NK", "TD", "TV", "QD", "TH"
    };

    /// <summary>Visit BHYT-scope cost below this is fully covered; at or above it the card benefit applies (QĐ2).</summary>
    public const decimal FullCoverageThreshold = 379_500m;

    /// <summary>Normalizes (trim, upper-case, drop spaces/dashes) and validates; returns the card and its benefit %.</summary>
    /// <exception cref="DomainValidationException">400 MSG-ERR-FIN-01 on any of the 4 format errors.</exception>
    public static (string Card, int BenefitPercent) Parse(string raw)
    {
        var card = new string((raw ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        if (card.Length != 15) throw Invalid("Mã thẻ BHYT phải gồm đúng 15 ký tự.");
        if (!GroupCodes.Contains(card[..2])) throw Invalid("Hai ký tự đầu không thuộc danh mục mã đối tượng BHYT.");
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
