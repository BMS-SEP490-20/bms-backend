using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.PatientDomain;

/// <summary>
/// Controller managing clinical patient profiles and medical identity cards (UC08 / Iteration 2).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    /// <summary>
    /// Searches or retrieves patient profiles by code, phone number, or identity card number.
    /// </summary>
    [HttpGet]
    public IActionResult SearchPatients([FromQuery] string? search)
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 2",
            message = "Patient directory endpoint is scheduled for Iteration 2."
        });
    }

    /// <summary>
    /// Retrieves patient profile by identifier.
    /// </summary>
    [HttpGet("{id}")]
    public IActionResult GetPatientById(string id)
    {
        return Ok(new
        {
            success = true,
            id,
            status = "Scheduled",
            milestone = "Iteration 2"
        });
    }
}
