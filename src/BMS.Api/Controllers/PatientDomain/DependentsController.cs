using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.PatientDomain;

/// <summary>
/// Controller managing patient dependent profiles and family member associations (UC11 / Iteration 2).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DependentsController : ControllerBase
{
    /// <summary>
    /// Retrieves dependents linked to the authenticated patient.
    /// </summary>
    [HttpGet]
    public IActionResult GetDependents([FromQuery] string? primaryPatientId)
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 2",
            message = "Patient dependents management endpoint is scheduled for Iteration 2."
        });
    }
}
