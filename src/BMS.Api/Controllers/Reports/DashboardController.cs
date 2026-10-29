using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BMS.Api.Controllers.Reports;

[ApiController]
[Route("api/dashboard")]
// WHY: UC12: every role; the role comes from the JWT (R15). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleSets.AnyRole)]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// UC12 - Xem Dashboard theo vai trò người dùng (View Role-Based Dashboard)
    /// </summary>
    /// <remarks>
    /// WHY (TIEU-CHI R15): role and user come only from the JWT. The former <c>?role=</c>/<c>?userId=</c> query
    /// parameters let any caller read another role's or user's dashboard; they are still accepted for
    /// backward compatibility with the web client but ignored.
    /// </remarks>
    [HttpGet("stats")]
    public async Task<IActionResult> GetDashboardStats([FromQuery] string? role, [FromQuery] string? userId)
    {
        _ = role; _ = userId;
        var targetRole = User.FindFirstValue("role") ?? User.FindFirstValue(ClaimTypes.Role);
        var targetUserId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(targetRole) || string.IsNullOrEmpty(targetUserId)) return Forbid();

        var stats = await _dashboardService.GetDashboardStatsAsync(targetUserId, targetRole);
        return Ok(new
        {
            success = true,
            data = stats
        });
    }
}
