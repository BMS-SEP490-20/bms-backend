using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Users;

/// <summary>
/// Controller scheduling clinical staff duty shifts and on-call rotations (UC16 / Iteration 2).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ShiftsController : ControllerBase
{
    /// <summary>
    /// Retrieves shift assignments for clinical personnel.
    /// </summary>
    [HttpGet]
    public IActionResult GetShifts([FromQuery] DateTime? date, [FromQuery] string? employeeId)
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 2",
            message = "Staff duty shift scheduling endpoint is scheduled for Iteration 2."
        });
    }
}
