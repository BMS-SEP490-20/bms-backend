using BMS.Core.Constants;
using BMS.Core.Exceptions;
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
// WHY: UC30/UC25: search/view Manager, Doctor, Lab, Receptionist; Patient /me; guest profile Receptionist. Admin denied (decision 10). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class PatientsController(
    IPatientService patients,
    IAuthorizationService authorization) : ControllerBase
{
    /// <summary>
    /// Searches and filters patient profiles by code, name, phone number, or CCCD/CMND (Staff / Manager / Doctor / Lab / Receptionist).
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaffOrReception)]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PatientInformationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PatientInformationDto>>> SearchPatients(
        [FromQuery] PatientSearchRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await patients.SearchPatientsAsync(request, ct);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                title = ex.Code,
                code = ex.Code,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = 500,
                title = "INTERNAL_SERVER_ERROR",
                code = "INTERNAL_SERVER_ERROR",
                detail = "Không thể tải danh sách hồ sơ bệnh nhân lúc này. Vui lòng thử lại sau.",
                message = "Không thể tải danh sách hồ sơ bệnh nhân lúc này. Vui lòng thử lại sau."
            });
        }
    }

    /// <summary>
    /// Retrieves the authenticated user's own patient profile, vitals, dependents, and recent clinical history.
    /// </summary>
    [Authorize(Roles = RoleNames.Patient)]
    [HttpGet("me")]
    [ProducesResponseType(typeof(PatientInformationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientInformationDto>> GetMyProfile(CancellationToken ct)
    {
        try
        {
            var demoHeader = Request.Headers["X-Demo-User"].FirstOrDefault();
            var userId = CurrentUserId ?? (!string.IsNullOrWhiteSpace(demoHeader) ? demoHeader.Trim() : null);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new
                {
                    success = false,
                    status = 401,
                    title = "UNAUTHORIZED",
                    code = "UNAUTHORIZED",
                    detail = "Vui lòng đăng nhập để xem thông tin hồ sơ bệnh nhân.",
                    message = "Vui lòng đăng nhập để xem thông tin hồ sơ bệnh nhân."
                });
            }
            var result = await patients.GetMyPatientInformationAsync(userId, ct);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                title = ex.Code,
                code = ex.Code,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = 500,
                title = "INTERNAL_SERVER_ERROR",
                code = "INTERNAL_SERVER_ERROR",
                detail = "Không thể tải thông tin hồ sơ bệnh nhân lúc này.",
                message = "Không thể tải thông tin hồ sơ bệnh nhân lúc này."
            });
        }
    }

    /// <summary>
    /// Retrieves detailed patient information by Patient ID, Patient Code, or CCCD/CMND.
    /// Staff/Doctors/Receptionists can view any patient; Patients can only view their own profile.
    /// </summary>
    // Patient is admitted here but the service limits a Patient to their own profile (CanViewAllPatientsAsync = false).
    [Authorize(Roles = RoleSets.ClinicalStaffOrReception + "," + RoleNames.Patient)]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(PatientInformationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientInformationDto>> GetPatientById(
        string id,
        CancellationToken ct)
    {
        try
        {
            var isStaffOrAdmin = await CanViewAllPatientsAsync();
            var result = await patients.GetPatientInformationAsync(id, CurrentUserId, isStaffOrAdmin, ct);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                title = ex.Code,
                code = ex.Code,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = 500,
                title = "INTERNAL_SERVER_ERROR",
                code = "INTERNAL_SERVER_ERROR",
                detail = "Không thể tra cứu thông tin hồ sơ bệnh nhân lúc này.",
                message = "Không thể tra cứu thông tin hồ sơ bệnh nhân lúc này."
            });
        }
    }

    /// <summary>
    /// Creates a new walk-in / guest patient profile (Create Guest Profile use case).
    /// Strictly restricted to authenticated Receptionist role.
    /// Accessible via POST /api/patients/guests, POST /api/patients/guest, or POST /api/patients.
    /// </summary>
    [Authorize(Roles = RoleNames.Receptionist)]
    [HttpPost("guests")]
    [HttpPost("guest")]
    [HttpPost]
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
                success = false,
                status = 403,
                title = "Forbidden",
                code = "FORBIDDEN",
                detail = "Chỉ tài khoản Lễ Tân (Receptionist) mới có quyền tạo hồ sơ khách vãng lai (Create Guest Profile).",
                message = "Chỉ tài khoản Lễ Tân (Receptionist) mới có quyền tạo hồ sơ khách vãng lai (Create Guest Profile)."
            });
        }

        try
        {
            var created = await patients.CreateGuestProfileAsync(request, CurrentUserId, ct);
            return CreatedAtAction(nameof(GetPatientById), new { id = created.Id }, created);
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                title = ex.Code,
                code = ex.Code,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = 500,
                title = "INTERNAL_SERVER_ERROR",
                code = "INTERNAL_SERVER_ERROR",
                detail = "Không thể khởi tạo hồ sơ khách vãng lai lúc này. Vui lòng thử lại sau.",
                message = "Không thể khởi tạo hồ sơ khách vãng lai lúc này. Vui lòng thử lại sau."
            });
        }
    }

    private bool IsReceptionist()
    {
        // WHY (TIEU-CHI R10, matrix UC25): only Lễ tân creates guest profiles; Admin was removed (decision 10).
        return User.Identity?.IsAuthenticated == true && User.IsInRole(RoleNames.Receptionist);
    }

    private string? CurrentUserId =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<bool> CanViewAllPatientsAsync()
    {
        // WHY: anonymous callers used to get "view all" (PII leak, matrix UC30). The class attribute now
        // rejects anonymous and Admin callers; staff roles admitted there may list patients, a Patient may not.
        if (User.Identity?.IsAuthenticated != true)
            return false;
        if (!User.IsInRole(RoleNames.Patient))
            return true;

        var policy = $"{PermissionPolicy.Prefix}{Permissions.ViewPatients},{Permissions.ManageAppointment},{Permissions.CheckIn}";
        return (await authorization.AuthorizeAsync(User, (object?)null, policy)).Succeeded;
    }
}
