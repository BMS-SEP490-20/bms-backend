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
    [HttpGet("{identifier}")]
    [AllowAnonymous]
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
        bool isStaff = User.Identity?.IsAuthenticated == true &&
                       (User.IsInRole("Admin") || User.IsInRole("Doctor") || User.IsInRole("LabHead") ||
                        User.IsInRole("LabTech") || User.IsInRole("LabTechnician") || User.IsInRole("Receptionist"));

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
