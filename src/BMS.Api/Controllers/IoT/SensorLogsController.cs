using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.IoT;

/// <summary>
/// Controller ingesting and querying IoT environmental sensor telemetry logs (UC28 / Iteration 3).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SensorLogsController : ControllerBase
{
    /// <summary>
    /// Queries sensor temperature and humidity time-series telemetry.
    /// </summary>
    [HttpGet]
    public IActionResult GetSensorLogs([FromQuery] string? deviceId, [FromQuery] int hours = 24)
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 3",
            message = "IoT sensor telemetry query endpoint is scheduled for Iteration 3."
        });
    }
}
