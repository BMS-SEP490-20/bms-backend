using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing clinical laboratory test orders and physician requisitions (UC18 / Iteration 2).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TestOrdersController : ControllerBase
{
    /// <summary>
    /// Retrieves test orders by patient or status.
    /// </summary>
    [HttpGet]
    public IActionResult GetOrders([FromQuery] string? patientId, [FromQuery] string? status)
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 2",
            message = "Lab test orders endpoint is scheduled for Iteration 2."
        });
    }
}
