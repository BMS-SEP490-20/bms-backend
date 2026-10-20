using System.Security.Claims;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller providing diagnostic laboratory test report generation, verification, and PDF export (UC42 / Iteration 2).
/// Conforms to: Production Standards, Clean Architecture & AGENTS.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TestReportsController : ControllerBase
{
    private readonly ITestReportService _testReportService;

    public TestReportsController(ITestReportService testReportService)
    {
        _testReportService = testReportService;
    }

    /// <summary>
    /// Retrieves full diagnostic report for a specific test order.
    /// </summary>
    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> GetReportByOrder(string orderId)
    {
        var report = await _testReportService.GetReportByOrderIdAsync(orderId);
        if (report == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"No test report found for order '{orderId}'."
            });
        }

        return Ok(new
        {
            success = true,
            data = report
        });
    }

    /// <summary>
    /// Retrieves test report by report ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetReportById(string id)
    {
        var report = await _testReportService.GetReportByIdAsync(id);
        if (report == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Test report '{id}' not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = report
        });
    }

    /// <summary>
    /// Compiles and generates official diagnostic report for an order.
    /// </summary>
    [HttpPost("order/{orderId}/generate")]
    public async Task<IActionResult> GenerateReport(string orderId, [FromBody] GenerateTestReportRequestDto request)
    {
        request.OrderId = orderId;
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var report = await _testReportService.GenerateReportAsync(request, currentUserId);

        return Ok(new
        {
            success = true,
            message = $"Test report '{report.ReportId}' generated successfully.",
            data = report
        });
    }

    /// <summary>
    /// Exports report PDF payload and formatted printable HTML document.
    /// </summary>
    [HttpGet("order/{orderId}/pdf")]
    public async Task<IActionResult> ExportPdf(string orderId)
    {
        var export = await _testReportService.ExportReportPdfAsync(orderId);
        if (export == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Cannot export report for order '{orderId}' - order not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = export
        });
    }
}
