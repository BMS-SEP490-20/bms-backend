using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Inventory;

/// <summary>
/// Controller managing laboratory analyzers, centrifuges, and diagnostic equipments (UC25 / Iteration 3).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// WHY: Stub outside UC list; lab equipment visible to Admin, Manager, Lab like inventory. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleSets.AdminManagerLab)]
public class EquipmentsController : ControllerBase
{
    /// <summary>
    /// Retrieves list of laboratory medical diagnostic equipments.
    /// </summary>
    [HttpGet]
    public IActionResult GetEquipments()
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 3",
            message = "Lab equipment inventory endpoint is scheduled for Iteration 3."
        });
    }
}
