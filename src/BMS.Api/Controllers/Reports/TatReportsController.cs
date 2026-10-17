using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Reports;

/// <summary>
/// Controller aggregating laboratory Turnaround Time (TAT) metrics and SLA compliance (UC27 / Iteration 3).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TatReportsController : ControllerBase
{
    /// <summary>
    /// Retrieves average specimen turnaround times segmented by lab section and test package.
    /// </summary>
    [HttpGet]
    public IActionResult GetTatReport([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 3",
            message = "Laboratory TAT analytics endpoint is scheduled for Iteration 3."
        });
    }
}
