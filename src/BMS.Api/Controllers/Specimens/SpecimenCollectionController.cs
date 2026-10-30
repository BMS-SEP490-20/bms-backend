using System.Security.Claims;
using BMS.Application.DTOs.Specimens;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Specimens;

/// <summary>
/// Specimen collection with SMP- barcodes (UC37/UC39). 409 ORDER_NOT_PAID for a Routine order awaiting payment,
/// 400 SAMPLE_PATIENT_MISMATCH when the 2-factor check fails, 409 when already collected or cancelled.
/// </summary>
[ApiController]
[Route("api/specimens")]
// WHY: collection is Lab only (ROLE-PERMISSION-MATRIX §1.5; Nurse merged into Receptionist does not sample).
[Authorize(Roles = RoleNames.Lab)]
public class SpecimenCollectionController(ISpecimenCollectionService collection) : ControllerBase
{
    /// <summary>Collects all tubes of an order in CLSI order of draw; collector = signed-in Lab user.</summary>
    [HttpPost("collect")]
    [ProducesResponseType(typeof(CollectSpecimenResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Collect([FromBody] CollectSpecimenRequestDto request, CancellationToken ct)
    {
        var labUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        return Ok(new { success = true, data = await collection.CollectAsync(request, labUserId, ct) });
    }
}
