
using System.Globalization;
using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using BMS.Core.Entities;

namespace BMS.Application.Services;

/// <summary>
/// Builds the doctor schedule day-view grid (UC16).
/// Expands each shift into fixed time-slot rows that match the UI layout.
/// </summary>
public class DoctorScheduleService : IDoctorScheduleService
{
    private readonly IDoctorScheduleRepository _repo;

    /// <summary>Fixed time-row headers displayed on the left of the grid.</summary>
    private static readonly string[] TimeRows =
    {
        "07:00-09:00",
        "09:00-11:00",
        "11:00-13:00",
        "13:00-14:00",
        "14:00-17:00"
    };

    public DoctorScheduleService(IDoctorScheduleRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    /// <inheritdoc />
    public async Task<DoctorScheduleResponseDto> GetDayScheduleAsync(
        DateTime? date = null,
        string? doctorId = null,
        string? search = null,
        CancellationToken ct = default)
    {
        var target = (date ?? DateTime.UtcNow).Date;

        var doctors = await _repo.GetDoctorsAsync(search, ct);
        if (!string.IsNullOrWhiteSpace(doctorId))
            doctors = doctors.Where(d => d.Id == doctorId).ToList();

        var shifts = await _repo.GetShiftsByDateAsync(target, doctorId, ct);
        var rooms = await _repo.GetRoomsMapAsync(ct);

        var shiftByDoctor = shifts
            .GroupBy(s => s.DoctorId)
            .ToDictionary(g => g.Key, g => g.First());

        var columns = new List<DoctorScheduleColumnDto>();

        foreach (var doc in doctors)
        {
            shiftByDoctor.TryGetValue(doc.Id, out var shift);
            rooms.TryGetValue(doc.RoomId, out var room);
            if (shift != null && rooms.TryGetValue(shift.RoomId, out var shiftRoom))
                room = shiftRoom;

            columns.Add(new DoctorScheduleColumnDto
            {
                DoctorId = doc.Id,
                FullName = doc.FullName,
                Specialty = doc.Specialty,
                Status = shift?.Status ?? "Approved",
                RoomId = room?.Id ?? doc.RoomId,
                RoomName = room?.RoomName ?? doc.RoomId,
                Slots = BuildSlots(doc, shift, room)
            });
        }

        var culture = CultureInfo.InvariantCulture;
        var dateLabel = target.ToString("dddd, dd/MM/yyyy", culture);

        return new DoctorScheduleResponseDto
        {
            Date = target.ToString("yyyy-MM-dd"),
            DateLabel = dateLabel,
            TimeRows = TimeRows,
            Doctors = columns,
            TotalDoctors = columns.Count,
            Note = columns.Count > 0
                ? "Non-overlapping schedule. Lunch break slots (11:00-13:00) do not accept patients."
                : "No doctors scheduled for this date."
        };
    }

    /// <summary>
    /// Expands a shift into the five fixed time-slot cards shown in the UI.
    /// </summary>
    private static IReadOnlyList<ScheduleSlotDto> BuildSlots(
        Doctor doc, DoctorShift? shift, DoctorRoom? room)
    {
        var roomLabel = room?.RoomName ?? doc.RoomId;
        var count = shift?.CurrentPatientsCount ?? 0;
        var max = shift?.MaxPatients ?? 0;
        var remaining = Math.Max(0, max - count);
        var shiftType = shift?.ShiftType ?? "Morning";

        return new List<ScheduleSlotDto>
        {
            new()
            {
                StartTime = "07:00", EndTime = "09:00",
                Activity = shiftType is "Morning" or "FullDay" ? "Consultation & Orders" : "Available",
                RoomName = roomLabel,
                Detail = shiftType is "Morning" or "FullDay"
                    ? $"{Math.Min(count, 4)} visits"
                    : "Open",
                IsAvailable = shiftType is not ("Morning" or "FullDay")
            },
            new()
            {
                StartTime = "09:00", EndTime = "11:00",
                Activity = shiftType is "Morning" or "FullDay" ? "Result Review" : "Consultation & Orders",
                RoomName = roomLabel,
                Detail = $"{count} records",
                IsAvailable = false
            },
            new()
            {
                StartTime = "11:00", EndTime = "13:00",
                Activity = "Lunch Break",
                RoomName = string.Empty,
                Detail = string.Empty,
                IsBreak = true
            },
            new()
            {
                StartTime = "13:00", EndTime = "14:00",
                Activity = shiftType is "Afternoon" or "FullDay" ? "Result Counseling" : "Result Review",
                RoomName = roomLabel,
                Detail = remaining > 0 ? "Open" : $"{count} records",
                IsAvailable = remaining > 0 && shiftType is not ("Afternoon" or "FullDay")
            },
            new()
            {
                StartTime = "14:00", EndTime = "17:00",
                Activity = shiftType is "Afternoon" or "FullDay" ? "Consultation & Orders" : "Available",
                RoomName = roomLabel,
                Detail = shiftType is "Afternoon" or "FullDay"
                    ? $"{count} visits"
                    : "Open",
                IsAvailable = shiftType is not ("Afternoon" or "FullDay")
            }
        };
    }
}