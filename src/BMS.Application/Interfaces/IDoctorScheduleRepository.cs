using BMS.Core.Entities;
using System.Numerics;

namespace BMS.Application.Interfaces;

/// <summary>
/// Repository contract for doctor schedule data (UC16).
/// </summary>
public interface IDoctorScheduleRepository
{
    /// <summary>Returns all active doctor profiles, optionally filtered by name/specialty.</summary>
    Task<IReadOnlyList<Doctor>> GetDoctorsAsync(string? search = null, CancellationToken ct = default);

    /// <summary>Returns a single doctor by Id.</summary>
    Task<Doctor?> GetDoctorByIdAsync(string doctorId, CancellationToken ct = default);

    /// <summary>Returns shifts for a given date (and optional doctor filter).</summary>
    Task<IReadOnlyList<DoctorShift>> GetShiftsByDateAsync(
        DateTime date,
        string? doctorId = null,
        CancellationToken ct = default);

    /// <summary>Returns all active rooms keyed by Id.</summary>
    Task<IReadOnlyDictionary<string, DoctorRoom>> GetRoomsMapAsync(CancellationToken ct = default);
}