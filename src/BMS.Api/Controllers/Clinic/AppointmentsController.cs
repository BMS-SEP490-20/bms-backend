using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Clinic;

/// <summary>
/// Controller managing patient consultation appointments and home collection schedules (UC21 / Iteration 2).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    /// <summary>
    /// Retrieves scheduled appointments by date range or patient ID.
    /// </summary>
    [HttpGet]
    public IActionResult GetAppointments([FromQuery] DateTime? date, [FromQuery] string? patientId)
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 2",
            message = "Appointments API endpoint is scheduled for Iteration 2."
        });
    }

    /// <summary>
    /// Creates a new patient appointment booking.
    /// </summary>
    [HttpPost]
    public IActionResult CreateAppointment([FromBody] object payload)
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 2",
            message = "Appointment creation endpoint is scheduled for Iteration 2."
        });
    }
}
