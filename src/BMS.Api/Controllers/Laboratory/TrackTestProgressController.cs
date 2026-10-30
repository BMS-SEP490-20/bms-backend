using BMS.Api.Authorization;
using BMS.Core.Constants;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller for tracking specimen lifecycle and test progress across the 6-stage clinical supply chain (UC48).
/// Conforms to: ISO 15189, HIPAA Privacy Standards & AGENTS.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/tracking")]
// WHY: UC48: lookup by code is public and PII-masked (decision 18); patient list/search need login. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class TrackTestProgressController : ControllerBase
{
    private readonly ITrackTestProgressService _trackingService;

    public TrackTestProgressController(ITrackTestProgressService trackingService)
    {
        _trackingService = trackingService;
    }

    /// <summary>
    /// Public or clinical lookup of a test specimen's 6-stage progress by Order Number, Barcode, or Sample ID.
    /// Unauthenticated requests automatically have patient names masked to preserve clinical privacy.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{identifier}")]
    public async Task<IActionResult> GetTracking(string identifier, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return BadRequest(new
            {
                success = false,
                message = "Mã biên nhận hoặc mã vạch Barcode không được để trống."
            });
        }

        // Check if user is authenticated staff
        // WHY: unmasked tracking is for clinical staff and Lễ tân only (matrix UC48); Admin is excluded (decision 10)
        // and anonymous/Patient callers always get the PII-masked public view.
        bool isStaff = User.Identity?.IsAuthenticated == true &&
                       (User.IsInRole(RoleNames.Manager) || User.IsInRole(RoleNames.Lab) ||
                        User.IsInRole(RoleNames.Doctor) || User.IsInRole(RoleNames.Receptionist));

        var result = await _trackingService.GetTrackingAsync(identifier.Trim(), isPublicQuery: !isStaff, ct);
        if (result == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Không tìm thấy tiến trình xét nghiệm cho mã yêu cầu này. Vui lòng kiểm tra lại mã biên lai hoặc barcode."
            });
        }

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// Retrieves all active and recent sample trackings for a patient.
    /// </summary>
    [Authorize(Roles = RoleSets.TrackingViewers)]
    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientTrackings(string patientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return BadRequest(new
            {
                success = false,
                message = "Mã bệnh nhân không hợp lệ."
            });
        }

        if (!await this.MayAccessPatientAsync(patientId.Trim(), ct)) return Forbid();
        var list = await _trackingService.GetPatientActiveTrackingsAsync(patientId.Trim(), ct);
        return Ok(new
        {
            success = true,
            total = list.Count,
            data = list
        });
    }

    /// <summary>
    /// Searches and filters recent sample progress records across the laboratory (Clinical Staff only).
    /// </summary>
    // Lab-wide search is staff work; a Patient uses patient/{own id} instead.
    [Authorize(Roles = RoleSets.ClinicalStaffOrReception)]
    [HttpGet("search")]
    public async Task<IActionResult> SearchTrackings(
        [FromQuery] string? query,
        [FromQuery] string? status,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var list = await _trackingService.SearchTrackingsAsync(query, status, fromDate, toDate, limit, ct);
        return Ok(new
        {
            success = true,
            total = list.Count,
            data = list
        });
    }
}
