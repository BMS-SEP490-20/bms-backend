using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Users;

/// <summary>
/// Controller managing clinical staff profiles, medical qualifications, and work assignments (UC14 / Iteration 2).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// WHY: Stub: employee directory, Admin. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleNames.Admin)]
public class EmployeesController : ControllerBase
{
    /// <summary>
    /// Retrieves list of clinical employees (Doctors, Technicians, Receptionists, Phlebotomists).
    /// </summary>
    [HttpGet]
    public IActionResult GetEmployees([FromQuery] string? department)
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 2",
            message = "Clinical employees management endpoint is scheduled for Iteration 2."
        });
    }
}
