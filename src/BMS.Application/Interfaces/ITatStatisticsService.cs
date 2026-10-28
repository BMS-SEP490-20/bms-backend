using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Statistics;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service contract for ISO 15189 Turnaround Time (TAT) computation, analytics, compliance benchmarking, and reporting (UC63).
/// </summary>
public interface ITatStatisticsService
{
    /// <summary>
    /// Computes full comprehensive master turnaround time report including summaries, breakdowns, trends, and violations.
    /// </summary>
    Task<TatStatisticsReportDto> GetTatReportAsync(TatQueryParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Retrieves top-level KPI summary metrics (mean, median, P90, min, max, compliance rate).
    /// </summary>
    Task<TatSummaryStatisticsDto> GetTatSummaryAsync(TatQueryParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Retrieves daily/periodic trend data points for chart visualization.
    /// </summary>
    Task<List<TatTrendPointDto>> GetTatTrendsAsync(TatQueryParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Retrieves granular list of outlier sample violations that breached target TAT thresholds.
    /// </summary>
    Task<List<TatViolationDetailDto>> GetTatViolationsAsync(TatQueryParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Generates UTF-8 CSV audit export of turnaround time records.
    /// </summary>
    Task<byte[]> ExportTatReportCsvAsync(TatQueryParameters parameters, CancellationToken ct = default);
}
