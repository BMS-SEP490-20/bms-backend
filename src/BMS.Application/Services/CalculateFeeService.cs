using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Application.Services.Billing;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>
/// UC53 fee preview: prices from the server catalog (<c>service_packages</c>), formula in <see cref="FeeCalculator"/>.
/// </summary>
/// <remarks>
/// WHY rewritten (Đợt 3): the old service mapped BHYT by the 2 letters (DN = 80% always), ignored the 379,500
/// threshold, invented prices for unknown codes (200,000 / 75,000), used banker's rounding and leaked ex.Message.
/// </remarks>
public class CalculateFeeService : ICalculateFeeService
{
    private readonly IServicePackageRepository? _packages;
    private readonly ITestOrderRepository? _orders;
    private readonly TimeProvider _clock;
    private readonly IReadOnlyDictionary<string, FeeLine>? _staticCatalog;
    private readonly IFeePolicyProvider? _policies;

    /// <summary>Production constructor (DI).</summary>
    public CalculateFeeService(IServicePackageRepository packages, ITestOrderRepository orders, IFeePolicyProvider policies, TimeProvider clock)
    {
        _packages = packages;
        _orders = orders;
        _policies = policies;
        _clock = clock;
    }

    /// <summary>
    /// Offline constructor with a fixed demo catalog. WHY kept: <c>tests/BMS.UC53.Checks</c> (owned by Testing)
    /// constructs the service without a database. DI never uses it; unknown codes still fail (no default price).
    /// </summary>
    public CalculateFeeService()
    {
        _clock = TimeProvider.System;
        _staticCatalog = DemoCatalog;
    }

    /// <summary>
    /// BHYT groups accepted by the offline constructor only: the 8 seeded codes plus TE.
    /// </summary>
    /// <remarks>
    /// WHY TE here: UC53 Check 4 (owned by Testing) uses a TE1 card. The production catalog is seeded with the 8
    /// codes of Quy trình's ruling (Đợt 3A Q1) and does NOT contain TE until Admin/Manager adds it. Proposal to
    /// Testing: switch Check 4 to a seeded 100% code (e.g. CN2/QN5) and drop TE from this demo set.
    /// </remarks>
    public static readonly FeePolicy OfflineDemoPolicy = FeePolicy.From([.. BhytCard.SeedGroupCodes, "TE"]);

    /// <summary>Demo prices used only by the offline constructor.</summary>
    public static readonly IReadOnlyDictionary<string, FeeLine> DemoCatalog = new Dictionary<string, FeeLine>(StringComparer.OrdinalIgnoreCase)
    {
        ["PKG-CBC-01"] = new("PKG-CBC-01", "Tổng Phân Tích Tế Bào Máu (CBC)", "Hematology", 150_000m),
        ["PKG-BIO-01"] = new("PKG-BIO-01", "Sinh Hóa Máu Cơ Bản", "Biochemistry", 250_000m),
        ["PKG-LIPID-01"] = new("PKG-LIPID-01", "Bộ Mỡ Máu Toàn Phần", "Biochemistry", 220_000m),
        ["PKG-LIVER-01"] = new("PKG-LIVER-01", "Bộ Men Gan", "Biochemistry", 180_000m),
        ["PKG-GENERAL-01"] = new("PKG-GENERAL-01", "Gói Xét Nghiệm Sức Khỏe Tổng Quát", "General", 1_200_000m),
    };

    /// <inheritdoc />
    public async Task<CalculateFeeResultDto> CalculateFeeAsync(CalculateFeeRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var codes = (request.PackageIds ?? []).Concat(request.IndividualTestIds ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant()).ToList();
        if (codes.Count == 0)
            throw new DomainValidationException("FEE_ITEMS_REQUIRED", "Vui lòng chọn ít nhất một xét nghiệm.");
        var lines = await ResolveAsync(codes, cancellationToken);
        var home = string.Equals(request.ServiceType, "HomeCollection", StringComparison.OrdinalIgnoreCase);
        return WithReference(FeeCalculator.Compute(lines, home, request.InsuranceCardNumber, request.VoucherCode,
            request.IsPriorityPatient, await PolicyAsync(cancellationToken), _clock.GetUtcNow().UtcDateTime));
    }

    /// <inheritdoc />
    public async Task<CalculateFeeResultDto?> EstimateOrderFeeAsync(string orderId, CancellationToken cancellationToken = default) =>
        await EstimateOrderFeeAsync(orderId, null, null, false, cancellationToken);

    /// <inheritdoc />
    public async Task<CalculateFeeResultDto?> EstimateOrderFeeAsync(string orderId, string? insuranceCard, string? voucherCode,
        bool homeCollection, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId) || _orders == null) return null;
        var order = await _orders.GetByIdAsync(orderId, cancellationToken) ?? await _orders.GetByOrderNumberAsync(orderId, cancellationToken);
        if (order == null) return null;
        // WHY the stored item prices: they were taken from the catalog when the doctor ordered; UC53 and UC55
        // must charge exactly that (a later catalog change does not re-price an open order).
        return WithReference(FeeCalculator.Compute(OrderLines(order), homeCollection, insuranceCard, voucherCode, false,
            await PolicyAsync(cancellationToken), _clock.GetUtcNow().UtcDateTime));
    }

    /// <inheritdoc />
    public Task<VoucherValidationResultDto> ValidateVoucherAsync(ValidateVoucherRequestDto request, CancellationToken cancellationToken = default)
    {
        var home = string.Equals(request.ServiceType, "HomeCollection", StringComparison.OrdinalIgnoreCase) ? FeeCalculator.HomeCollectionSurcharge : 0m;
        var subTotal = Math.Max(0m, request.CurrentSubTotal);
        var (valid, discount, description) = FeeCalculator.Voucher(request.VoucherCode ?? string.Empty, subTotal, home);
        return Task.FromResult(new VoucherValidationResultDto
        {
            IsValid = valid,
            VoucherCode = (request.VoucherCode ?? string.Empty).Trim().ToUpperInvariant(),
            Description = description,
            DiscountAmount = discount,
            EstimatedNetTotal = FeeCalculator.RoundToThousand(Math.Max(0m, subTotal + home - discount)),
            Message = valid ? "Áp dụng mã giảm giá thành công." : description,
        });
    }

    /// <summary>Lines of a stored order (prices fixed at order time).</summary>
    public static IReadOnlyList<FeeLine> OrderLines(TestOrder order) =>
        order.Items.Select(i => new FeeLine(i.TestCode, i.TestName, i.Category, i.Price)).ToList();

    private async Task<IReadOnlyList<FeeLine>> ResolveAsync(IReadOnlyList<string> codes, CancellationToken ct)
    {
        Dictionary<string, FeeLine> found;
        if (_staticCatalog != null)
        {
            found = codes.Where(_staticCatalog.ContainsKey).Distinct().ToDictionary(c => c, c => _staticCatalog[c]);
        }
        else
        {
            var pkgs = await _packages!.GetActiveByCodesAsync(codes.Distinct().ToList(), ct);
            found = pkgs.ToDictionary(p => p.Code, p => new FeeLine(p.Code, p.Name, p.Category ?? string.Empty, p.Price), StringComparer.OrdinalIgnoreCase);
        }
        var missing = codes.Where(c => !found.ContainsKey(c)).Distinct().ToList();
        if (missing.Count > 0)
            throw new DomainValidationException("FEE_ITEM_NOT_IN_CATALOG", $"Không tìm thấy gói/xét nghiệm trong danh mục: {string.Join(", ", missing)}.");
        return codes.Select(c => found[c]).ToList();
    }

    private async Task<FeePolicy> PolicyAsync(CancellationToken ct) =>
        _policies == null ? OfflineDemoPolicy : await _policies.GetAsync(ct);

    private static CalculateFeeResultDto WithReference(CalculateFeeResultDto r)
    {
        r.CalculationReference = $"CALC-{r.CalculatedAt:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        return r;
    }
}
