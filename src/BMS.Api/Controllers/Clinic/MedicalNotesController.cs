using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Clinic;

/// <summary>
/// Controller managing clinical doctor notes and patient consultation records (UC20 / Iteration 2).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MedicalNotesController : ControllerBase
{
    /// <summary>
    /// Retrieves medical notes for a patient.
    /// </summary>
    [HttpGet]
    public IActionResult GetNotes([FromQuery] string patientId)
    {
        return Ok(new
        {
            success = true,
            patientId,
            status = "Scheduled",
            milestone = "Iteration 2",
            message = "Clinical medical notes endpoint is scheduled for Iteration 2."
        });
    }
}
