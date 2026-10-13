using BMS.Application.DTOs;

namespace BMS.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardStatsDto> GetDashboardStatsAsync(string userId, string role);
}
