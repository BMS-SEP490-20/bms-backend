using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Laboratory;

/// <summary>
/// Query filters for paginated laboratory test criteria searches.
/// </summary>
public class TestCriterionQueryParameters
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public string? Category { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// Payload to create a new clinical test parameter / criterion (UC18).
/// </summary>
public class CreateTestCriterionDto
{
    [Required(ErrorMessage = "CriterionCode is required.")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "CriterionCode must be between 2 and 20 characters.")]
    public string CriterionCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "CriterionName is required.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "CriterionName must be between 2 and 150 characters.")]
    public string CriterionName { get; set; } = string.Empty;

    [StringLength(150)]
    public string EnglishName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Category is required.")]
    public string Category { get; set; } = "Biochemistry";

    public string Unit { get; set; } = string.Empty;

    public string ValueType { get; set; } = "Numeric";

    public double? NormalMin { get; set; }
    public double? NormalMax { get; set; }
    public string? NormalText { get; set; }

    public double? PanicLow { get; set; }
    public double? PanicHigh { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Payload to update an existing clinical test parameter (UC18).
/// </summary>
public class UpdateTestCriterionDto
{
    [Required(ErrorMessage = "CriterionName is required.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "CriterionName must be between 2 and 150 characters.")]
    public string CriterionName { get; set; } = string.Empty;

    [StringLength(150)]
    public string EnglishName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Category is required.")]
    public string Category { get; set; } = "Biochemistry";

    public string Unit { get; set; } = string.Empty;

    public string ValueType { get; set; } = "Numeric";

    public double? NormalMin { get; set; }
    public double? NormalMax { get; set; }
    public string? NormalText { get; set; }

    public double? PanicLow { get; set; }
    public double? PanicHigh { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Sanitized clinical criterion model returned in responses.
/// </summary>
public class TestCriterionResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string CriterionCode { get; set; } = string.Empty;
    public string CriterionName { get; set; } = string.Empty;
    public string EnglishName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string ValueType { get; set; } = "Numeric";
    public double? NormalMin { get; set; }
    public double? NormalMax { get; set; }
    public string? NormalText { get; set; }
    public string FormattedNormalRange { get; set; } = string.Empty;
    public double? PanicLow { get; set; }
    public double? PanicHigh { get; set; }
    public string FormattedPanicRange { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
