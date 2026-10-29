using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Patients;

public sealed class PatientVitalsDto
{
    [Range(20, 260, ErrorMessage = "Chiều cao phải từ 20 đến 260 cm.")]
    public double? HeightCm { get; set; }

    [Range(0.5, 400, ErrorMessage = "Cân nặng phải từ 0.5 đến 400 kg.")]
    public double? WeightKg { get; set; }

    [StringLength(30)]
    public string? BloodPressure { get; set; }

    [Range(20, 260, ErrorMessage = "Nhịp tim phải từ 20 đến 260 bpm.")]
    public int? HeartRateBpm { get; set; }

    [Range(30, 45, ErrorMessage = "Nhiệt độ cơ thể phải từ 30 đến 45 °C.")]
    public double? TemperatureC { get; set; }

    [Range(50, 100, ErrorMessage = "SpO2 phải từ 50 đến 100%.")]
    public int? Spo2Percent { get; set; }
}

public sealed class PatientDependentDto
{
    public string DependentId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string Gender { get; set; } = "Unspecified";
    public string Relationship { get; set; } = string.Empty;
    public string? IdentityNumber { get; set; }
}

/// <summary>
/// Request payload for creating a walk-in / guest patient profile (Create Guest Profile use case).
/// </summary>
public sealed class CreateGuestProfileRequest : IValidatableObject
{
    private static readonly HashSet<string> ValidGenders = new(StringComparer.OrdinalIgnoreCase)
        { "Male", "Female", "Other", "Unspecified" };

    private static readonly HashSet<string> ValidBloodTypes = new(StringComparer.OrdinalIgnoreCase)
        { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-", "A", "B", "AB", "O", "Unknown" };

    [Required(ErrorMessage = "Họ và tên bệnh nhân là bắt buộc.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 150 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ngày sinh của bệnh nhân là bắt buộc.")]
    public DateTime? DateOfBirth { get; set; }

    [Required(ErrorMessage = "Giới tính sinh học là bắt buộc (Male, Female, Other).")]
    [StringLength(20)]
    public string Gender { get; set; } = "Unspecified";

    [Required(ErrorMessage = "Số điện thoại liên hệ là bắt buộc.")]
    [RegularExpression(@"^0[35789][0-9]{8}$", ErrorMessage = "Số điện thoại phải gồm 10 chữ số, bắt đầu bằng 03, 05, 07, 08 hoặc 09.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [StringLength(150)]
    public string? Email { get; set; }

    /// <summary>Số CCCD (12 chữ số) hoặc CMND (9 chữ số). Tuỳ chọn với trẻ em/khách vãng lai chưa có CCCD.</summary>
    [RegularExpression(@"^[0-9]{12}$", ErrorMessage = "Số CCCD phải gồm đúng 12 chữ số.")]
    public string? IdentityNumber { get; set; }

    [StringLength(500, ErrorMessage = "Địa chỉ tối đa 500 ký tự.")]
    public string? Address { get; set; }

    [StringLength(15)]
    public string? BloodType { get; set; }

    [StringLength(150)]
    public string? EmergencyContactName { get; set; }

    [RegularExpression(@"^(\+84|0)[0-9]{8,10}$", ErrorMessage = "Số điện thoại khẩn cấp không hợp lệ.")]
    public string? EmergencyContactPhone { get; set; }

    [StringLength(2000)]
    public string? MedicalHistory { get; set; }

    public List<string>? Allergies { get; set; }

    public PatientVitalsDto? Vitals { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateOfBirth is { } dob)
        {
            var dobUtc = dob.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dob, DateTimeKind.Utc) : dob.ToUniversalTime();
            if (dobUtc > DateTime.UtcNow.AddMinutes(5))
            {
                yield return new ValidationResult("Ngày sinh không được ở tương lai.", [nameof(DateOfBirth)]);
            }
            else if (dobUtc.Year < 1900)
            {
                yield return new ValidationResult("Ngày sinh không hợp lệ (phải từ năm 1900 trở đi).", [nameof(DateOfBirth)]);
            }
        }

        if (!string.IsNullOrWhiteSpace(Gender) && !ValidGenders.Contains(Gender.Trim()))
        {
            yield return new ValidationResult("Giới tính chỉ nhận một trong các giá trị: Male, Female, Other.", [nameof(Gender)]);
        }

        if (!string.IsNullOrWhiteSpace(BloodType) && !ValidBloodTypes.Contains(BloodType.Trim()))
        {
            yield return new ValidationResult("Nhóm máu không hợp lệ (A+, A-, B+, B-, AB+, AB-, O+, O-, Unknown).", [nameof(BloodType)]);
        }
    }
}

/// <summary>
/// Query parameters for searching and filtering patient records.
/// </summary>
public sealed class PatientSearchRequest
{
    [StringLength(100)]
    public string? Search { get; set; }

    [StringLength(100)]
    public string? Keyword { get; set; }

    [StringLength(20)]
    public string? Gender { get; set; }

    [StringLength(15)]
    public string? BloodType { get; set; }

    public bool? IsGuest { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    public string? EffectiveKeyword =>
        !string.IsNullOrWhiteSpace(Keyword) ? Keyword.Trim() :
        !string.IsNullOrWhiteSpace(Search) ? Search.Trim() : null;
}

public sealed class PatientAppointmentSummaryDto
{
    public string AppointmentId { get; set; } = string.Empty;
    public DateTime? AppointmentDate { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? PaymentStatus { get; set; }
    public IReadOnlyList<string> TestPackages { get; set; } = Array.Empty<string>();
}

public sealed class PatientMedicalRecordSummaryDto
{
    public string RecordId { get; set; } = string.Empty;
    public string? AppointmentId { get; set; }
    public string? DoctorId { get; set; }
    public string? Symptoms { get; set; }
    public string? Diagnosis { get; set; }
    public string? DoctorNotes { get; set; }
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Comprehensive patient demographic, identity, clinical vitals, and history response (View Patient Information use case).
/// </summary>
public sealed class PatientInformationDto
{
    public string Id { get; set; } = string.Empty;
    public string PatientCode { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public bool IsGuest { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public int? Age { get; set; }
    public string Gender { get; set; } = "Unspecified";
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? IdentityNumber { get; set; }
    public string? Address { get; set; }
    public string? BloodType { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? MedicalHistory { get; set; }
    public IReadOnlyList<string> Allergies { get; set; } = Array.Empty<string>();
    public PatientVitalsDto? Vitals { get; set; }
    public IReadOnlyList<PatientDependentDto> Dependents { get; set; } = Array.Empty<PatientDependentDto>();
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public IReadOnlyList<PatientAppointmentSummaryDto> RecentAppointments { get; set; } = Array.Empty<PatientAppointmentSummaryDto>();
    public IReadOnlyList<PatientMedicalRecordSummaryDto> RecentMedicalRecords { get; set; } = Array.Empty<PatientMedicalRecordSummaryDto>();
}
