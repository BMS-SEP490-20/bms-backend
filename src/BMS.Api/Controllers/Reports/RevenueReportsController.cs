using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Reports;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Reports;

/// <summary>
/// Controller aggregating laboratory financial revenue, package sales, and cashier metrics (UC78 / Issue #I3-091).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/revenue")]
[Route("api/revenue")]
// WHY: UC78: Admin; Manager read-only (all actions are GET). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleSets.AdminOrManager)]
public class RevenueReportsController : ControllerBase
{
    private readonly IRevenueReportService _revenueReportService;

    public RevenueReportsController(IRevenueReportService revenueReportService)
    {
        _revenueReportService = revenueReportService;
    }

    /// <summary>
    /// Retrieves comprehensive aggregated revenue report data across all financial dimensions.
    /// </summary>
    [HttpGet]
    [HttpGet("report")]
    public async Task<IActionResult> GetRevenueReport([FromQuery] RevenueFilterParameters parameters, CancellationToken ct)
    {
        var report = await _revenueReportService.GetComprehensiveReportAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = report,
            message = "Đã tải báo cáo doanh thu phòng xét nghiệm thành công."
        });
    }

    /// <summary>
    /// Retrieves executive revenue summary KPIs (Gross, Net, BHYT, Discounts, Surcharges, AOV, Growth).
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] RevenueFilterParameters parameters, CancellationToken ct)
    {
        var summary = await _revenueReportService.GetSummaryAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = summary
        });
    }

    /// <summary>
    /// Retrieves time-series revenue trend data points for chart visualisations.
    /// </summary>
    [HttpGet("trend")]
    public async Task<IActionResult> GetTrend([FromQuery] RevenueFilterParameters parameters, CancellationToken ct)
    {
        var trend = await _revenueReportService.GetTrendAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = trend,
            total = trend.Count
        });
    }

    /// <summary>
    /// Retrieves revenue breakdown grouped by test category.
    /// </summary>
    [HttpGet("by-category")]
    public async Task<IActionResult> GetByCategory([FromQuery] RevenueFilterParameters parameters, CancellationToken ct)
    {
        var categories = await _revenueReportService.GetByCategoryAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = categories
        });
    }

    /// <summary>
    /// Retrieves revenue distribution by payment method.
    /// </summary>
    [HttpGet("by-payment-method")]
    public async Task<IActionResult> GetByPaymentMethod([FromQuery] RevenueFilterParameters parameters, CancellationToken ct)
    {
        var methods = await _revenueReportService.GetByPaymentMethodAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = methods
        });
    }

    /// <summary>
    /// Retrieves revenue distribution by service channel (Tại viện vs Tận nơi).
    /// </summary>
    [HttpGet("by-channel")]
    public async Task<IActionResult> GetByChannel([FromQuery] RevenueFilterParameters parameters, CancellationToken ct)
    {
        var channels = await _revenueReportService.GetByChannelAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = channels
        });
    }

    /// <summary>
    /// Retrieves granular transaction rows for accounting reconciliation and Excel export.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> GetExport([FromQuery] RevenueFilterParameters parameters, CancellationToken ct)
    {
        var export = await _revenueReportService.GetExportTransactionsAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = export,
            total = export.Count
        });
    }
}
