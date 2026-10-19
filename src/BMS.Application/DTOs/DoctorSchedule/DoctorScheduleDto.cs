/// <summary>
/// A single time-slot cell in the doctor schedule grid.
/// </summary>
public class ScheduleSlotDto
{
    /// <summary>Start time (HH:mm).</summary>
    public string StartTime { get; set; } = string.Empty;

    /// <summary>End time (HH:mm).</summary>
    public string EndTime { get; set; } = string.Empty;

    /// <summary>
    /// Activity label shown on the card
    /// (e.g. "Khám và chỉ định", "Đọc kết quả", "Nghỉ trưa", "Lịch khả dụng").
    /// </summary>
    public string Activity { get; set; } = string.Empty;

    /// <summary>Room name or number for this slot.</summary>
    public string RoomName { get; set; } = string.Empty;

    /// <summary>Optional detail line (e.g. "4 lượt", "6 hồ sơ", "Còn trống").</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>True when the slot is a break / lunch.</summary>
    public bool IsBreak { get; set; }

    /// <summary>True when the slot has no appointment yet (available).</summary>
    public bool IsAvailable { get; set; }
}

/// <summary>
/// One doctor's column in the day-view grid.
/// </summary>
public class DoctorScheduleColumnDto
{
    /// <summary>Doctor Id.</summary>
    public string DoctorId { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Specialty.</summary>
    public string Specialty { get; set; } = string.Empty;

    /// <summary>Shift approval status (Approved, Pending…).</summary>
    public string Status { get; set; } = "Approved";

    /// <summary>Default room Id.</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>Default room name.</summary>
    public string RoomName { get; set; } = string.Empty;

    /// <summary>Ordered list of slots for the selected day.</summary>
    public IReadOnlyList<ScheduleSlotDto> Slots { get; set; } = Array.Empty<ScheduleSlotDto>();
}

/// <summary>
/// Full response for the doctor schedule day view (UC16).
/// </summary>
public class DoctorScheduleResponseDto
{
    /// <summary>Selected date (yyyy-MM-dd).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Display date label (e.g. "Thứ Sáu, 16/10/2026").</summary>
    public string DateLabel { get; set; } = string.Empty;

    /// <summary>Fixed time-row headers for the grid.</summary>
    public IReadOnlyList<string> TimeRows { get; set; } = Array.Empty<string>();

    /// <summary>Doctor columns with their slots.</summary>
    public IReadOnlyList<DoctorScheduleColumnDto> Doctors { get; set; } = Array.Empty<DoctorScheduleColumnDto>();

    /// <summary>Total doctor columns returned.</summary>
    public int TotalDoctors { get; set; }

    /// <summary>Optional informational note shown below the grid.</summary>
    public string? Note { get; set; }
}