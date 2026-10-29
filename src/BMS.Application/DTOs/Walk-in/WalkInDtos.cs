using System.ComponentModel.DataAnnotations;

namespace BMS.WalkIn.DTOs;

/// <summary>
/// UC26 request – maps to CreateGuestProfileRequest fields used by PatientService.
/// </summary>
public class RegisterWalkInRequest
{
    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public DateTime DateOfBirth { get; set; }

    [Required]
    public string Gender { get; set; } = "Unspecified";

    [Required, RegularExpression(@"^0[35789]\d{8}$")]
    public string Phone { get; set; } = string.Empty;

    // WHY 12 only: BA:L25 — CCCD is exactly 12 digits (old 9-digit CMND no longer accepted).
    [Required, RegularExpression(@"^\d{12}$")]
    public string IdentityNumber { get; set; } = string.Empty;

    [EmailAddress, MaxLength(120)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(10)]
    public string? BloodType { get; set; }

    [MaxLength(1000)]
    public string? MedicalHistory { get; set; }

    public List<string>? Allergies { get; set; }

    public WalkInVitalsDto? Vitals { get; set; }

    public string Priority { get; set; } = "NORMAL";

    [MaxLength(150)]
    public string? EmergencyContactName { get; set; }

    [MaxLength(20)]
    public string? EmergencyContactPhone { get; set; }
}

public class WalkInVitalsDto
{
    public double? HeightCm { get; set; }
    public double? WeightKg { get; set; }
    public string? BloodPressure { get; set; }
    public int? HeartRateBpm { get; set; }
    public double? TemperatureC { get; set; }
    public int? Spo2Percent { get; set; }
}

public class RegisterWalkInResponse
{
    public string PatientId { get; set; } = string.Empty;
    public string PatientCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string IdentityNumber { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public bool IsGuest { get; set; } = true;
    public string Priority { get; set; } = "NORMAL";
    public string Message { get; set; } = "Walk-in guest profile created successfully.";
}

public class PatientLookupResponse
{
    public bool Exists { get; set; }
    public string? PatientId { get; set; }
    public string? FullName { get; set; }
    public string? IdentityNumber { get; set; }
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
}

public class ApiErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
