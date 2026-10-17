using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.IoT;

/// <summary>
/// Controller handling IoT threshold alarms and specimen cold-chain alerts (UC28 / Iteration 3).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AlertsController : ControllerBase
{
    /// <summary>
    /// Retrieves active IoT alerts (e.g. refrigerator temp outside 2-8°C range).
    /// </summary>
    [HttpGet]
    public IActionResult GetActiveAlerts()
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 3",
            message = "IoT alerts monitoring endpoint is scheduled for Iteration 3."
        });
    }
}
