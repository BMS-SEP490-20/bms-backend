using BMS.Application.DTOs.Billing;
using BMS.Application.Utilities;

namespace BMS.Application.Services.Billing;

/// <summary>A priced line from the server catalog or a stored order.</summary>
public sealed record FeeLine(string Code, string Name, string Category, decimal Price, bool BhytEligible = true);

/// <summary>
/// The one UC53 formula, shared by fee preview (UC53), order payment and invoice payment (UC55) so all screens
/// show the same number (GAP C6.8).
/// </summary>
/// <remarks>
/// Gross = TestSubTotal + HomeCollectionSurcharge (150,000).
/// Insurance = BHYT-scope tests × EffectivePercent (threshold 379,500 on the BHYT-scope cost, home surcharge excluded,
/// checked before any voucher). Net = max(0, Gross − Insurance − Voucher − PriorityDiscount), rounded to 1,000 with
/// halves away from zero (2,500 → 3,000; never banker's rounding). All money is decimal; client amounts are ignored.
/// </remarks>
public static class FeeCalculator
{
    public const decimal HomeCollectionSurcharge = 150_000m;
    public const decimal Bms10Cap = 200_000m;

    /// <summary>Computes the full breakdown.</summary>
    /// <param name="lines">Priced lines (server prices only).</param>
    /// <param name="homeCollection">Adds the 150,000 surcharge (never covered by BHYT).</param>
    /// <param name="insuranceCard">Raw card or null; invalid → 400 MSG-ERR-FIN-01.</param>
    /// <param name="voucherCode">BMS10 or FREEHOME; anything else is reported invalid and gives no discount.</param>
    /// <param name="isPriorityPatient">Courtesy discount flag (see <see cref="PriorityDiscount"/>).</param>
    /// <param name="now">Calculation time (UTC).</param>
    public static CalculateFeeResultDto Compute(IReadOnlyList<FeeLine> lines, bool homeCollection, string? insuranceCard,
        string? voucherCode, bool isPriorityPatient, DateTime now)
    {
        var r = new CalculateFeeResultDto
        {
            CalculatedAt = now,
            ServiceType = homeCollection ? "HomeCollection" : "Clinic",
            IsPriorityPatient = isPriorityPatient,
            HomeCollectionSurcharge = homeCollection ? HomeCollectionSurcharge : 0m,
            Items = lines.Select(l => new FeeItemBreakdownDto
            {
                ItemId = l.Code, ItemCode = l.Code, ItemName = l.Name, Category = l.Category, ItemType = "Package",
                UnitPrice = l.Price, Quantity = 1, IsBhytEligible = l.BhytEligible, PatientPayAmount = l.Price
            }).ToList()
        };
        r.TestSubTotal = lines.Sum(l => l.Price);
        r.GrossTotal = r.TestSubTotal + r.HomeCollectionSurcharge;

        if (!string.IsNullOrWhiteSpace(insuranceCard))
        {
            var (card, benefit) = BhytCard.Parse(insuranceCard);
            var scope = lines.Where(l => l.BhytEligible).Sum(l => l.Price);
            var effective = BhytCard.EffectivePercent(benefit, scope);
            r.HasInsurance = true;
            r.InsuranceCardNumber = card;
            r.InsuranceBenefitPercent = benefit;
            r.InsuranceCoveragePercent = effective;
            r.InsuranceTierDescription = effective == 100 && benefit < 100
                ? $"Mức hưởng thẻ {benefit}%; chi phí BHYT dưới {BhytCard.FullCoverageThreshold:N0}đ nên BHYT chi trả 100%"
                : $"BHYT chi trả {effective}% (mã quyền lợi {card[2]})";
            r.InsuranceDeduction = scope * effective / 100m;
            foreach (var item in r.Items.Where(i => i.IsBhytEligible))
            {
                item.BhytCoveredAmount = item.TotalPrice * effective / 100m;
                item.PatientPayAmount = item.TotalPrice - item.BhytCoveredAmount;
            }
        }

        if (!string.IsNullOrWhiteSpace(voucherCode))
        {
            var (valid, discount, description) = Voucher(voucherCode, r.TestSubTotal, r.HomeCollectionSurcharge);
            r.VoucherCode = voucherCode.Trim().ToUpperInvariant();
            r.IsVoucherValid = valid;
            r.VoucherDescription = description;
            r.VoucherDiscount = discount;
        }

        r.PriorityDiscount = isPriorityPatient ? PriorityDiscount(r.TestSubTotal - r.InsuranceDeduction) : 0m;
        r.TotalDiscount = r.VoucherDiscount + r.PriorityDiscount;
        r.NetBeforeRounding = Math.Max(0m, r.GrossTotal - r.InsuranceDeduction - r.VoucherDiscount - r.PriorityDiscount);
        r.NetTotalPayable = RoundToThousand(r.NetBeforeRounding);
        r.TotalInWords = VietnameseCurrencyWords.Convert(r.NetTotalPayable);
        return r;
    }

    /// <summary>Rounds to the nearest 1,000 VND, halves up (QĐ5): 2,499 → 2,000; 2,500 → 3,000; 3,500 → 4,000.</summary>
    public static decimal RoundToThousand(decimal amount) =>
        Math.Round(amount / 1000m, 0, MidpointRounding.AwayFromZero) * 1000m;

    /// <summary>Voucher rules (BA:L65): BMS10 = 10% of the test fee before BHYT, capped at 200,000; FREEHOME = the home surcharge.</summary>
    public static (bool Valid, decimal Discount, string Description) Voucher(string code, decimal testSubTotal, decimal homeSurcharge) =>
        code.Trim().ToUpperInvariant() switch
        {
            "BMS10" => (true, Math.Min(Bms10Cap, testSubTotal * 0.10m), "Giảm 10% phí xét nghiệm (tối đa 200.000đ)"),
            "FREEHOME" => (homeSurcharge > 0, homeSurcharge, homeSurcharge > 0 ? "Miễn phụ phí lấy mẫu tại nhà" : "FREEHOME chỉ áp dụng khi lấy mẫu tại nhà"),
            var c => (false, 0m, $"Mã ưu đãi '{c}' không hợp lệ hoặc đã hết hạn.")
        };

    /// <summary>
    /// Courtesy discount for priority patients: 10% of the remaining test fee, max 100,000 (kept from the existing code).
    /// </summary>
    /// <remarks>TODO(phat, open question Q-FIN-2): BA names PriorityDiscount in the formula but gives no rate.</remarks>
    public static decimal PriorityDiscount(decimal remainingTestFee) => Math.Min(100_000m, Math.Max(0m, remainingTestFee) * 0.10m);
}
