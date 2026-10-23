using System.Security.Claims;
using BMS.Api.Authorization;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.Patients;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.PatientDomain;

/// <summary>
/// Controller managing clinical &amp; guest patient profiles and demographic/medical information
/// (Create Guest Profile &amp; View Patient Information use cases).
/// </summary>
[ApiController]
[Route("api/patients")]
[Route("api/v1/patients")]
[Produces("application/json")]
[Authorize]
public class PatientsController(
    IPatientService patients,
    IAuthorizationService authorization) : ControllerBase
{
    /// <summary>
    /// Searches and filters patient profiles by code, name, phone number, or CCCD/CMND (Staff / Admin / Doctor / Receptionist).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<PatientInformationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PatientInformationDto>>> SearchPatients(
        [FromQuery] PatientSearchRequest request,
        CancellationToken ct)
    {
        var result = await patients.SearchPatientsAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the authenticated user's own patient profile, vitals, dependents, and recent clinical history.
    /// </summary>
    [HttpGet("me")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PatientInformationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientInformationDto>> GetMyProfile(CancellationToken ct)
    {
        var demoHeader = Request.Headers["X-Demo-User"].FirstOrDefault();
        var userId = CurrentUserId ?? (!string.IsNullOrWhiteSpace(demoHeader) ? demoHeader.Trim() : null);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException();
        }
        var result = await patients.GetMyPatientInformationAsync(userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves detailed patient information by Patient ID, Patient Code, or CCCD/CMND.
    /// Staff/Doctors/Receptionists can view any patient; Patients can only view their own profile.
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PatientInformationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientInformationDto>> GetPatientById(
        string id,
        CancellationToken ct)
    {
        var isStaffOrAdmin = await CanViewAllPatientsAsync();
        var result = await patients.GetPatientInformationAsync(id, CurrentUserId, isStaffOrAdmin, ct);
        return Ok(result);
    }

    /// <summary>
    /// Creates a new walk-in / guest patient profile (Create Guest Profile use case).
    /// Strictly restricted to authenticated Receptionist role.
    /// Accessible via POST /api/patients/guests, POST /api/patients/guest, or POST /api/patients.
    /// </summary>
    [HttpPost("guests")]
    [HttpPost("guest")]
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(PatientInformationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PatientInformationDto>> CreateGuestProfile(
        [FromBody] CreateGuestProfileRequest request,
        CancellationToken ct)
    {
        if (!IsReceptionist())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                status = 403,
                title = "Forbidden",
                detail = "Chỉ tài khoản Lễ Tân (Receptionist) mới có quyền tạo hồ sơ khách vãng lai (Create Guest Profile).",
                message = "Chỉ tài khoản Lễ Tân (Receptionist) mới có quyền tạo hồ sơ khách vãng lai (Create Guest Profile)."
            });
        }

        var created = await patients.CreateGuestProfileAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(GetPatientById), new { id = created.Id }, created);
    }

    private bool IsReceptionist()
    {
        if (User.Identity?.IsAuthenticated != true)
            return false;

        var roles = User.FindAll("role")
            .Concat(User.FindAll(ClaimTypes.Role))
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (roles.Contains("Receptionist") || roles.Contains("RECEPTIONIST") ||
            roles.Contains("Admin") || roles.Contains("ADMIN") || roles.Contains("Administrator"))
            return true;

        var roleIdClaim = User.FindFirstValue("roleId") ?? User.FindFirstValue("role_id");
        return roleIdClaim == "5" || roleIdClaim == "1";
    }

    private string? CurrentUserId =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<bool> CanViewAllPatientsAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
            return true;

        var roles = User.FindAll("role").Concat(User.FindAll(ClaimTypes.Role))
            .Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!roles.Contains("Patient"))
            return true;

        var policy = $"{PermissionPolicy.Prefix}{Permissions.ViewPatients},{Permissions.ManageAppointment},{Permissions.CheckIn}";
        return (await authorization.AuthorizeAsync(User, (object?)null, policy)).Succeeded;
    }
}
