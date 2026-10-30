using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Statistics;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Reports;

/// <summary>
/// Controller aggregating laboratory Turnaround Time (TAT) metrics and ISO 15189 SLA compliance (UC63 / Iteration 3 #I3-046).
/// Conforms to: Production Standards, Medical Laboratory Benchmarking & AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/statistics/tat")]
[Route("api/statistics/tat")]
[Route("api/v1/tat/statistics")]
[Route("api/tat/statistics")]
// WHY: UC63: Admin, Manager. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleSets.AdminOrManager)]
public class TatReportsController : ControllerBase
{
    private readonly ITatStatisticsService _tatService;

    public TatReportsController(ITatStatisticsService tatService)
    {
        _tatService = tatService;
    }

    /// <summary>
    /// Retrieves comprehensive laboratory Turnaround Time (TAT) statistics master report.
    /// Includes summary KPIs, priority breakdown, department breakdown, phase breakdown, trends, and recent violations.
    /// </summary>
    [HttpGet]
    [HttpGet("report")]
    public async Task<IActionResult> GetTatReport([FromQuery] TatQueryParameters parameters, CancellationToken ct)
    {
        var report = await _tatService.GetTatReportAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = report,
            message = "Đã trích xuất báo cáo thống kê thời gian quay vòng xét nghiệm (TAT) thành công."
        });
    }

    /// <summary>
    /// Retrieves top-level KPI metrics (Mean, Median, P90, Min, Max, Target Compliance Rate %).
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] TatQueryParameters parameters, CancellationToken ct)
    {
        var summary = await _tatService.GetTatSummaryAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = summary,
            message = "Đã tải số liệu KPI tổng quan TAT thành công."
        });
    }

    /// <summary>
    /// Retrieves periodic trend points for line and bar chart visualizations.
    /// </summary>
    [HttpGet("trends")]
    public async Task<IActionResult> GetTrends([FromQuery] TatQueryParameters parameters, CancellationToken ct)
    {
        var trends = await _tatService.GetTatTrendsAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = trends,
            message = "Đã tải dữ liệu xu hướng biến thiên TAT thành công."
        });
    }

    /// <summary>
    /// Retrieves outlier sample violations that breached target TAT thresholds.
    /// </summary>
    [HttpGet("violations")]
    public async Task<IActionResult> GetViolations([FromQuery] TatQueryParameters parameters, CancellationToken ct)
    {
        var violations = await _tatService.GetTatViolationsAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = violations,
            message = "Đã tải danh sách mẫu xét nghiệm vượt ngưỡng thời gian (Violations) thành công."
        });
    }

    /// <summary>
    /// Exports TAT statistics report as UTF-8 CSV with Excel BOM compatibility.
    /// </summary>
    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv([FromQuery] TatQueryParameters parameters, CancellationToken ct)
    {
        var csvBytes = await _tatService.ExportTatReportCsvAsync(parameters, ct);
        var filename = $"BaoCao_TAT_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        return File(csvBytes, "text/csv; charset=utf-8", filename);
    }
}