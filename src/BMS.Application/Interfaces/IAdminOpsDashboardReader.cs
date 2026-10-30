using BMS.Application.DTOs.Dashboard;

namespace BMS.Application.Interfaces;

/// <summary>
/// Reads aggregate operational counts for the Admin dashboard (UC12). Implemented in Infrastructure so only that
/// layer touches MongoDB (INV-2). Every method returns counts/sums only, never documents.
/// </summary>
public interface IAdminOpsDashboardReader
{
    /// <summary>Builds the ops snapshot.</summary>
    /// <param name="nowUtc">Reference time for the 24 h / 7 day windows and lockout checks.</param>
    /// <param name="ct">Cancels the database calls.</param>
    Task<AdminOpsDashboardDto> ReadAsync(DateTime nowUtc, CancellationToken ct);
}
