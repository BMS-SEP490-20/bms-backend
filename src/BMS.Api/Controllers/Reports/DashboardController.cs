using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Reports;

[ApiController]
[Route("api/dashboard")]
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
    /// <param name="role">Role: Patient, Doctor, LaboratoryUser, Receptionist, Manager, Administrator</param>
    /// <param name="userId">Mã định danh người dùng</param>
    [HttpGet("stats")]
    public async Task<IActionResult> GetDashboardStats([FromQuery] string? role, [FromQuery] string? userId)
    {
        var targetRole = string.IsNullOrWhiteSpace(role) ? "Patient" : role;
        var targetUserId = string.IsNullOrWhiteSpace(userId) ? "USR-PAT-01" : userId;

        var stats = await _dashboardService.GetDashboardStatsAsync(targetUserId, targetRole);
        return Ok(new
        {
            success = true,
            data = stats
        });
    }
}
