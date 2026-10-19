using System.ComponentModel.DataAnnotations;

namespace BMS.Schedule.DTOs;

public class CreateShiftRequest
{
    [Required]
    public string DoctorId { get; set; } = string.Empty;

    [Required]
    public DateTime ShiftDate { get; set; }

    [Required]
    [RegularExpression("^(Morning|Afternoon|Evening|Night|FullDay)$")]
    public string ShiftType { get; set; } = "Morning";

    [Required]
    public string RoomId { get; set; } = string.Empty;

    public string? ShiftStart { get; set; }
    public string? ShiftEnd { get; set; }

    [Range(1, 200)]
    public int MaxPatients { get; set; } = 30;

    [MaxLength(300)]
    public string? Notes { get; set; }

    public string? EmployeeName { get; set; }
    public string? Department { get; set; }
}

public class UpdateShiftRequest
{
    [RegularExpression("^(Morning|Afternoon|Evening|Night|FullDay)$")]
    public string? ShiftType { get; set; }

    public string? RoomId { get; set; }
    public string? ShiftStart { get; set; }
    public string? ShiftEnd { get; set; }

    [Range(1, 200)]
    public int? MaxPatients { get; set; }

    public string? Notes { get; set; }

    /// <summary>Approved | Scheduled | Active | Completed | Cancelled | Pending | Rejected</summary>
    public string? Status { get; set; }
}

public class ShiftDto
{
    public string Id { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string? EmployeeName { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public string? RoomName { get; set; }
    public DateTime ShiftDate { get; set; }
    public string ShiftType { get; set; } = string.Empty;
    public string? ShiftStart { get; set; }
    public string? ShiftEnd { get; set; }
    public int MaxPatients { get; set; }
    public int CurrentPatientsCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class ApiErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string[]>? Errors { get; set; }
}
