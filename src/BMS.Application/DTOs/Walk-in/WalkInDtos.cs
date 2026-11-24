using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMS.WalkIn.DTOs;

/// <summary>
/// UC26 request – maps to CreateGuestProfileRequest fields used by PatientService.
/// </summary>
public class RegisterWalkInRequest : IValidatableObject
{
    [Required(ErrorMessage = "Họ và tên người bệnh không được để trống."), MaxLength(150, ErrorMessage = "Họ và tên tối đa 150 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ngày sinh của người bệnh không được để trống.")]
    public DateTime DateOfBirth { get; set; }

    [Required(ErrorMessage = "Giới tính sinh học không được để trống.")]
    public string Gender { get; set; } = "Unspecified";

    [Required(ErrorMessage = "Số điện thoại liên hệ không được để trống."), RegularExpression(@"^0[35789]\d{8}$", ErrorMessage = "Số điện thoại phải gồm 10 chữ số, bắt đầu bằng 03, 05, 07, 08 hoặc 09.")]
    public string Phone { get; set; } = string.Empty;

    // WHY 12 only: BA:L25 — CCCD is exactly 12 digits (old 9-digit CMND no longer accepted).
    [Required(ErrorMessage = "Số CCCD / Định danh cá nhân không được để trống."), RegularExpression(@"^\d{12}$", ErrorMessage = "Số CCCD phải gồm đúng 12 chữ số theo Đề án 06 Bộ Y Tế.")]
    public string IdentityNumber { get; set; } = string.Empty;

    // Empty string must become null — [EmailAddress] rejects "" and blocks optional email on walk-in.
    private string? _email;

    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng."), MaxLength(120, ErrorMessage = "Email tối đa 120 ký tự.")]
    public string? Email
    {
        get => _email;
        set => _email = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [MaxLength(300, ErrorMessage = "Địa chỉ tối đa 300 ký tự.")]
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

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var today = DateTime.UtcNow.Date;
        if (DateOfBirth.Date > today)
        {
            yield return new ValidationResult(
                "Ngày sinh của bệnh nhân không được vượt quá thời điểm hiện tại.",
                [nameof(DateOfBirth)]);
        }
        else if (DateOfBirth.Year < 1900)
        {
            yield return new ValidationResult(
                "Ngày sinh không hợp lệ (phải từ năm 1900 trở đi).",
                [nameof(DateOfBirth)]);
        }
    }
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
    public string? Email { get; set; }
    public string? PortalUsername { get; set; }
    public string? TemporaryPassword { get; set; }
    public bool AccountAutoCreated { get; set; }
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
    public IDictionary<string, string[]>? Errors { get; set; }
}
