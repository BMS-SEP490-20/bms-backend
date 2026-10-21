using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Laboratory;

/// <summary>
/// Query parameters for filtering reference intervals list.
/// </summary>
public class ReferenceIntervalQueryParameters
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 15;
    public string? CriterionCode { get; set; }
    public string? Gender { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// Payload to create a new biological reference interval (UC57).
/// </summary>
public class CreateReferenceIntervalDto
{
    public string? CriterionId { get; set; }

    [Required(ErrorMessage = "Mã chỉ số xét nghiệm (CriterionCode) là bắt buộc.")]
    [StringLength(30, MinimumLength = 1, ErrorMessage = "CriterionCode phải từ 1 đến 30 ký tự.")]
    public string CriterionCode { get; set; } = string.Empty;

    public string? CriterionName { get; set; }

    public string? Unit { get; set; }

    [Required(ErrorMessage = "Giới tính áp dụng là bắt buộc (All, Male, Female).")]
    public string Gender { get; set; } = "All";

    [Range(0, 150, ErrorMessage = "Độ tuổi tối thiểu (AgeMin) phải từ 0 đến 150.")]
    public double AgeMin { get; set; } = 0;

    [Range(0, 150, ErrorMessage = "Độ tuổi tối đa (AgeMax) phải từ 0 đến 150.")]
    public double AgeMax { get; set; } = 120;

    public string AgeUnit { get; set; } = "Years";

    [StringLength(150, ErrorMessage = "Nhãn nhóm nhân khẩu học không được vượt quá 150 ký tự.")]
    public string? DemographicLabel { get; set; }

    public double? NormalMin { get; set; }
    public double? NormalMax { get; set; }
    public string? NormalText { get; set; }

    public double? PanicLow { get; set; }
    public double? PanicHigh { get; set; }

    public string? Condition { get; set; } = "General";
    public string? StandardAuthority { get; set; } = "Quyết định 2429/QĐ-BYT";
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Payload to update an existing reference interval (UC57).
/// </summary>
public class UpdateReferenceIntervalDto
{
    public string? CriterionName { get; set; }
    public string? Unit { get; set; }

    [Required(ErrorMessage = "Giới tính áp dụng là bắt buộc (All, Male, Female).")]
    public string Gender { get; set; } = "All";

    [Range(0, 150, ErrorMessage = "Độ tuổi tối thiểu (AgeMin) phải từ 0 đến 150.")]
    public double AgeMin { get; set; } = 0;

    [Range(0, 150, ErrorMessage = "Độ tuổi tối đa (AgeMax) phải từ 0 đến 150.")]
    public double AgeMax { get; set; } = 120;

    public string AgeUnit { get; set; } = "Years";

    [StringLength(150, ErrorMessage = "Nhãn nhóm nhân khẩu học không được vượt quá 150 ký tự.")]
    public string? DemographicLabel { get; set; }

    public double? NormalMin { get; set; }
    public double? NormalMax { get; set; }
    public string? NormalText { get; set; }

    public double? PanicLow { get; set; }
    public double? PanicHigh { get; set; }

    public string? Condition { get; set; }
    public string? StandardAuthority { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Clinical reference interval response model.
/// </summary>
public class ReferenceIntervalResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string CriterionId { get; set; } = string.Empty;
    public string CriterionCode { get; set; } = string.Empty;
    public string CriterionName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public string Gender { get; set; } = "All";
    public double AgeMin { get; set; }
    public double AgeMax { get; set; }
    public string AgeUnit { get; set; } = "Years";
    public string DemographicLabel { get; set; } = string.Empty;

    public double? NormalMin { get; set; }
    public double? NormalMax { get; set; }
    public string? NormalText { get; set; }
    public string FormattedNormalRange { get; set; } = string.Empty;

    public double? PanicLow { get; set; }
    public double? PanicHigh { get; set; }
    public string FormattedPanicRange { get; set; } = string.Empty;

    public string Condition { get; set; } = "General";
    public string StandardAuthority { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>
/// Request to evaluate a clinical lab test result against configured reference intervals (BR-LAB-02 / BR-LAB-03).
/// </summary>
public class EvaluateTestResultRequestDto
{
    [Required(ErrorMessage = "CriterionCode là bắt buộc.")]
    public string CriterionCode { get; set; } = string.Empty;

    public double? NumericValue { get; set; }
    public string? TextValue { get; set; }

    [Required(ErrorMessage = "Tuổi bệnh nhân là bắt buộc.")]
    [Range(0, 150)]
    public double PatientAge { get; set; }

    public string PatientAgeUnit { get; set; } = "Years";

    [Required(ErrorMessage = "Giới tính bệnh nhân là bắt buộc.")]
    public string PatientGender { get; set; } = "All";

    public string? Condition { get; set; } = "General";
}

/// <summary>
/// Clinical evaluation result with auto-flags, critical panic alerts, and guideline messages.
/// </summary>
public class EvaluateTestResultResponseDto
{
    public string CriterionCode { get; set; } = string.Empty;
    public string ValueEvaluated { get; set; } = string.Empty;

    /// <summary>
    /// Flags: "NORMAL", "L", "H", "PANIC_LOW", "PANIC_HIGH", "INDETERMINATE"
    /// </summary>
    public string Flag { get; set; } = "NORMAL";

    /// <summary>
    /// UI indicator color badge: "success" (normal), "warning" (low flag L), "danger" (high flag H or Panic)
    /// </summary>
    public string FlagColor { get; set; } = "success";

    /// <summary>
    /// Business rule message code: MSG-WRN-01, MSG-WRN-02, or null
    /// </summary>
    public string? MessageCode { get; set; }

    /// <summary>
    /// Localized clinical message
    /// </summary>
    public string AlertMessage { get; set; } = string.Empty;

    /// <summary>
    /// Whether life-threatening panic threshold is triggered (BR-LAB-03)
    /// </summary>
    public bool IsPanic { get; set; }

    /// <summary>
    /// Matched interval used for this evaluation
    /// </summary>
    public ReferenceIntervalResponseDto? MatchedInterval { get; set; }
}
