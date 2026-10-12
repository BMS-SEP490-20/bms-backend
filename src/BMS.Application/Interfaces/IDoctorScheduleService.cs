
using BMS.Application.DTOs;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service contract for UC16 – View Doctor Schedule.
/// Roles: Doctor, Manager, Administrator, Receptionist.
/// </summary>
public interface IDoctorScheduleService
{
    /// <summary>
    /// Builds the day-view schedule grid for the given date.
    /// </summary>
    /// <param name="date">Target date (defaults to today UTC).</param>
    /// <param name="doctorId">Optional filter – only this doctor's column.</param>
    /// <param name="search">Optional name/specialty keyword.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DoctorScheduleResponseDto> GetDayScheduleAsync(
        DateTime? date = null,
        string? doctorId = null,
        string? search = null,
        CancellationToken ct = default);
}