using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Reports;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for laboratory financial revenue analytics and executive reporting (UC78).
/// </summary>
public interface IRevenueReportService
{
    /// <summary>
    /// Retrieves a comprehensive composite revenue report containing all summary, trend, and breakdown dimensions.
    /// </summary>
    Task<ComprehensiveRevenueReportDto> GetComprehensiveReportAsync(RevenueFilterParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Retrieves aggregated financial summary KPIs for the filtered timeframe.
    /// </summary>
    Task<RevenueSummaryDto> GetSummaryAsync(RevenueFilterParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Retrieves time-series revenue trend data points for chart visualizations.
    /// </summary>
    Task<List<RevenueTrendPointDto>> GetTrendAsync(RevenueFilterParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Retrieves revenue breakdown grouped by laboratory test category.
    /// </summary>
    Task<List<RevenueByCategoryDto>> GetByCategoryAsync(RevenueFilterParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Retrieves revenue distribution by payment method (VietQR, Tiền mặt, BHYT, Thẻ).
    /// </summary>
    Task<List<RevenueByPaymentMethodDto>> GetByPaymentMethodAsync(RevenueFilterParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Retrieves revenue distribution by service channel (Tại viện vs Tận nơi).
    /// </summary>
    Task<List<RevenueByChannelDto>> GetByChannelAsync(RevenueFilterParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Retrieves granular transaction rows for accounting reconciliation and Excel export.
    /// </summary>
    Task<List<RevenueExportItemDto>> GetExportTransactionsAsync(RevenueFilterParameters parameters, CancellationToken ct = default);
}
