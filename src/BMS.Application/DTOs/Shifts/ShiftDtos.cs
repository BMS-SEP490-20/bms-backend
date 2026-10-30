using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace BMS.Application.DTOs.Shifts;

/// <summary>
/// Query parameters for viewing and filtering clinical/laboratory shifts (View Shifts use case).
/// </summary>
public sealed class ShiftFilterRequest : IValidatableObject
{
    /// <summary>Filter by exact calendar date (YYYY-MM-DD).</summary>
    public DateTime? Date { get; set; }

    /// <summary>Filter shifts starting from this date (inclusive).</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>Filter shifts up to this date (inclusive).</summary>
    public DateTime? ToDate { get; set; }

    /// <summary>Filter by employee/doctor account ID.</summary>
    [StringLength(100)]
    public string? EmployeeId { get; set; }

    /// <summary>Alias for EmployeeId.</summary>
    [StringLength(100)]
    public string? DoctorId { get; set; }

    /// <summary>Filter by assigned room ID (e.g., ROOM-101, LAB-ROOM-201).</summary>
    [StringLength(50)]
    public string? RoomId { get; set; }

    /// <summary>Filter by shift type (Morning, Afternoon, Evening, Night, FullDay).</summary>
    [StringLength(30)]
    public string? ShiftType { get; set; }

    /// <summary>Filter by shift status (Approved, Scheduled, Active, Completed, Cancelled, Pending).</summary>
    [StringLength(30)]
    public string? Status { get; set; }

    /// <summary>Filter by department or specialty.</summary>
    [StringLength(100)]
    public string? Department { get; set; }

    /// <summary>Keyword matching employee name, room name/ID, or department/specialty.</summary>
    [StringLength(100)]
    public string? Keyword { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    public string? EffectiveEmployeeId =>
        !string.IsNullOrWhiteSpace(EmployeeId) ? EmployeeId.Trim() :
        !string.IsNullOrWhiteSpace(DoctorId) ? DoctorId.Trim() : null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FromDate is { } from && ToDate is { } to && from.Date > to.Date)
        {
            yield return new ValidationResult(
                "FromDate không được lớn hơn ToDate.",
                [nameof(FromDate), nameof(ToDate)]);
        }
    }
}

/// <summary>
/// Shared write payload for creating and updating a staff work shift.
/// </summary>
public abstract class ShiftWriteRequest : IValidatableObject
{
    private static readonly HashSet<string> AllowedShiftTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Morning", "Afternoon", "Evening", "Night", "FullDay" };

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "Approved", "Scheduled", "Active", "Completed", "Cancelled", "Pending", "Rejected" };

    /// <summary>Employee / Doctor account ID.</summary>
    [StringLength(100)]
    public string? EmployeeId { get; set; }

    /// <summary>Alias for EmployeeId (maps to doctor_id in doctor_shifts).</summary>
    [StringLength(100)]
    public string? DoctorId { get; set; }

    /// <summary>Optional employee full name (auto-resolved from accounts/doctors if omitted).</summary>
    [StringLength(150)]
    public string? EmployeeName { get; set; }

    /// <summary>Optional employee role (Doctor, LabTech, Nurse, Receptionist...).</summary>
    [StringLength(50)]
    public string? EmployeeRole { get; set; }

    /// <summary>Optional department or clinical specialty.</summary>
    [StringLength(100)]
    public string? Department { get; set; }

    /// <summary>Assigned examination or laboratory room ID (e.g. ROOM-101, LAB-ROOM-201).</summary>
    [Required(ErrorMessage = "Phòng làm việc (RoomId) là bắt buộc.")]
    [StringLength(50, MinimumLength = 2)]
    public string RoomId { get; set; } = string.Empty;

    /// <summary>Calendar date of the shift (YYYY-MM-DD).</summary>
    [Required(ErrorMessage = "Ngày trực/ca làm việc (ShiftDate) là bắt buộc.")]
    public DateTime? ShiftDate { get; set; }

    /// <summary>Shift period: Morning, Afternoon, Evening, Night, FullDay.</summary>
    [Required(ErrorMessage = "Loại ca làm việc (ShiftType) là bắt buộc.")]
    [StringLength(30)]
    public string ShiftType { get; set; } = "Morning";

    /// <summary>Working start time in 24h HH:mm format (e.g. "07:00"). Defaults from ShiftType if omitted.</summary>
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Giờ bắt đầu (ShiftStart) phải theo định dạng 24h HH:mm (ví dụ: 07:00).")]
    public string? ShiftStart { get; set; }

    /// <summary>Working end time in 24h HH:mm format (e.g. "11:30"). Defaults from ShiftType if omitted.</summary>
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Giờ kết thúc (ShiftEnd) phải theo định dạng 24h HH:mm (ví dụ: 11:30).")]
    public string? ShiftEnd { get; set; }

    /// <summary>Maximum number of patients/samples handled in this shift.</summary>
    [Range(1, 500, ErrorMessage = "Số lượng bệnh nhân tối đa (MaxPatients) phải từ 1 đến 500.")]
    public int MaxPatients { get; set; } = 30;

    /// <summary>Shift status: Approved, Scheduled, Active, Completed, Cancelled, Pending.</summary>
    [StringLength(30)]
    public string Status { get; set; } = "Approved";

    /// <summary>Optional handover or scheduling notes.</summary>
    [StringLength(1000)]
    public string? Notes { get; set; }

    public string EffectiveEmployeeId =>
        !string.IsNullOrWhiteSpace(EmployeeId) ? EmployeeId.Trim() :
        !string.IsNullOrWhiteSpace(DoctorId) ? DoctorId.Trim() : string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(EffectiveEmployeeId))
        {
            yield return new ValidationResult(
                "Vui lòng chọn nhân viên/bác sĩ phân ca (EmployeeId hoặc DoctorId).",
                [nameof(EmployeeId)]);
        }

        if (string.IsNullOrWhiteSpace(ShiftType) || !AllowedShiftTypes.Contains(ShiftType.Trim()))
        {
            yield return new ValidationResult(
                "ShiftType chỉ nhận một trong các giá trị: Morning, Afternoon, Evening, Night, FullDay.",
                [nameof(ShiftType)]);
        }

        if (!string.IsNullOrWhiteSpace(Status) && !AllowedStatuses.Contains(Status.Trim()))
        {
            yield return new ValidationResult(
                "Status chỉ nhận một trong các giá trị: Approved, Scheduled, Active, Completed, Cancelled, Pending, Rejected.",
                [nameof(Status)]);
        }

        var (startStr, endStr) = ResolveWorkingTimeWindow(ShiftType, ShiftStart, ShiftEnd);
        if (TimeSpan.TryParseExact(startStr, @"hh\:mm", CultureInfo.InvariantCulture, out var startTs) &&
            TimeSpan.TryParseExact(endStr, @"hh\:mm", CultureInfo.InvariantCulture, out var endTs))
        {
            if (startTs >= endTs)
            {
                yield return new ValidationResult(
                    $"Giờ bắt đầu ({startStr}) phải nhỏ hơn giờ kết thúc ({endStr}).",
                    [nameof(ShiftStart), nameof(ShiftEnd)]);
            }
            else if ((endTs - startTs).TotalMinutes < 30)
            {
                yield return new ValidationResult(
                    "Thời lượng ca làm việc tối thiểu phải từ 30 phút trở lên.",
                    [nameof(ShiftStart), nameof(ShiftEnd)]);
            }
        }
    }

    public static (string Start, string End) ResolveWorkingTimeWindow(string? shiftType, string? shiftStart, string? shiftEnd)
    {
        var (defaultStart, defaultEnd) = (shiftType?.Trim().ToLowerInvariant()) switch
        {
            "morning" => ("07:00", "11:30"),
            "afternoon" => ("13:00", "17:00"),
            "evening" => ("17:00", "21:00"),
            "night" => ("21:00", "23:59"),
            "fullday" => ("07:00", "17:00"),
            _ => ("07:00", "11:30")
        };

        var start = !string.IsNullOrWhiteSpace(shiftStart) ? shiftStart.Trim() : defaultStart;
        var end = !string.IsNullOrWhiteSpace(shiftEnd) ? shiftEnd.Trim() : defaultEnd;
        return (start, end);
    }
}

public sealed class CreateShiftRequest : ShiftWriteRequest;

public sealed class UpdateShiftRequest : ShiftWriteRequest;

/// <summary>
/// Enriched shift response DTO containing working time and employee assignment details.
/// </summary>
public sealed class ShiftDto
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeRole { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string? LicenseNumber { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string? RoomNumber { get; set; }
    public string? Floor { get; set; }
    public DateTime ShiftDate { get; set; }
    public string ShiftType { get; set; } = "Morning";
    public string ShiftStart { get; set; } = "07:00";
    public string ShiftEnd { get; set; } = "11:30";
    public string WorkingHoursLabel { get; set; } = "07:00 - 11:30";
    public int DurationMinutes { get; set; }
    public int MaxPatients { get; set; }
    public int CurrentPatientsCount { get; set; }
    public int AvailableSlots => Math.Max(0, MaxPatients - CurrentPatientsCount);
    public string Status { get; set; } = "Approved";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class ShiftRoomDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? RoomNumber { get; set; }
    public string? Floor { get; set; }
    public string? Department { get; set; }
    public string Type { get; set; } = "Clinic";
}

public sealed class ShiftStaffDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Doctor";
    public int RoleId { get; set; }
    public string Department { get; set; } = string.Empty;
    public string? LicenseNumber { get; set; }
}
