using BMS.Core.Constants;
using System.Security.Claims;
using BMS.WalkIn.DTOs;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.WalkIn.Controllers;

/// <summary>
/// UC26 – Register walk-in / guest patient at reception.
/// Thin API over PatientService.CreateGuestProfileAsync.
/// </summary>
[ApiController]
[Route("api/walk-in")]
// WHY: UC26: Receptionist only (Admin removed). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleNames.Receptionist)]
public class WalkInController : ControllerBase
{
    private readonly IWalkInService _service;
    private readonly ILogger<WalkInController> _logger;

    public WalkInController(IWalkInService service, ILogger<WalkInController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterWalkInResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterWalkInRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub");
        var result = await _service.RegisterAsync(request, userId, ct);
        return CreatedAtAction(nameof(LookupIdentity), new { identityNumber = result.IdentityNumber }, result);
    }

    [HttpGet("lookup/identity/{identityNumber}")]
    [ProducesResponseType(typeof(PatientLookupResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PatientLookupResponse>> LookupIdentity(
        string identityNumber,
        CancellationToken ct)
    {
        var result = await _service.LookupByIdentityAsync(identityNumber, ct);
        return Ok(result);
    }

    [HttpGet("lookup/phone/{phone}")]
    [ProducesResponseType(typeof(PatientLookupResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PatientLookupResponse>> LookupPhone(
        string phone,
        CancellationToken ct)
    {
        var result = await _service.LookupByPhoneAsync(phone, ct);
        return Ok(result);
    }
}
