using BMS.Application.DTOs;

namespace BMS.Application.Interfaces;

public interface IDashboardService
{
    /// <summary>Role dashboard (UC12). Role and user come from the JWT; Admin gets operations data only.</summary>
    Task<DashboardStatsDto> GetDashboardStatsAsync(string userId, string role, CancellationToken ct = default);

    /// <summary>Admin operations snapshot (DOT2 follow-up 6): aggregate counts only, no patient/clinical data.</summary>
    Task<BMS.Application.DTOs.Dashboard.AdminOpsDashboardDto> GetAdminOpsAsync(CancellationToken ct);
}
