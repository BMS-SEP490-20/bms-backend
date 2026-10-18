using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs;

/// <summary>
/// Summary of a laboratory test criterion included inside a test package.
/// </summary>
public class TestPackageCriterionSummaryDto
{
    public string CriterionId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

/// DTO returned to clients when viewing or searching the test package catalog.
/// Does not expose sensitive internal fields.
public class TestPackageDto
{
    /// Package identifier (PKG-xxx).
    public string Id { get; set; } = string.Empty;

    /// Short package code (CBC-01...).
    public string PackageCode { get; set; } = string.Empty;

    /// Full name of the test package.
    public string PackageName { get; set; } = string.Empty;

    /// Service category (Hematology, Biochemistry...).
    public string Category { get; set; } = string.Empty;

    /// Service price in VND.
    public decimal Price { get; set; }

    /// Short description.
    public string Description { get; set; } = string.Empty;

    /// Sample tube type.
    public string SampleTube { get; set; } = string.Empty;

    /// Minimum sample volume (ml).
    public double SampleVolumeMl { get; set; }

    /// Whether the package is currently offered.
    public bool IsActive { get; set; }

    /// Included test criteria (biomarkers) in display order.
    public IReadOnlyList<TestPackageCriterionSummaryDto> Criteria { get; set; } = Array.Empty<TestPackageCriterionSummaryDto>();
}

/// <summary>
/// Request parameters for searching and filtering available test packages (Web &amp; Mobile).
/// </summary>
public class TestPackageSearchRequest : IValidatableObject
{
    /// <summary>Keyword matching package code, package name, description, category, sample tube, or biomarker code/name.</summary>
    [StringLength(100, ErrorMessage = "Từ khoá tìm kiếm tối đa 100 ký tự.")]
    public string? Keyword { get; set; }

    /// <summary>Alias for Keyword to support ?search= query parameter.</summary>
    [StringLength(100)]
    public string? Search { get; set; }

    /// <summary>Filter by category (e.g., Hematology, Biochemistry).</summary>
    [StringLength(100)]
    public string? Category { get; set; }

    /// <summary>Filter by sample tube type (e.g., EDTA, Heparin).</summary>
    [StringLength(100)]
    public string? SampleTube { get; set; }

    /// <summary>Minimum package price in VND.</summary>
    [Range(typeof(decimal), "0", "1000000000", ErrorMessage = "MinPrice không hợp lệ.")]
    public decimal? MinPrice { get; set; }

    /// <summary>Maximum package price in VND.</summary>
    [Range(typeof(decimal), "0", "1000000000", ErrorMessage = "MaxPrice không hợp lệ.")]
    public decimal? MaxPrice { get; set; }

    /// <summary>Filter by active status (non-admin callers always see active packages only).</summary>
    public bool? IsActive { get; set; }

    /// <summary>Sort field: name | code | price | category (default: category).</summary>
    public string? SortBy { get; set; }

    /// <summary>Sort direction: asc | desc (default: asc).</summary>
    [RegularExpression("(?i)^(asc|desc)$", ErrorMessage = "SortDir chỉ nhận asc hoặc desc.")]
    public string? SortDir { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Page phải >= 1.")]
    public int Page { get; set; } = 1;

    [Range(1, 100, ErrorMessage = "PageSize phải từ 1 đến 100.")]
    public int PageSize { get; set; } = 20;

    public string? EffectiveKeyword =>
        !string.IsNullOrWhiteSpace(Keyword) ? Keyword.Trim() :
        !string.IsNullOrWhiteSpace(Search) ? Search.Trim() : null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice is { } min && MaxPrice is { } max && min > max)
        {
            yield return new ValidationResult(
                "MinPrice không được lớn hơn MaxPrice.",
                [nameof(MinPrice), nameof(MaxPrice)]);
        }
    }
}

/// Response wrapper for the catalog list, supporting category filters.
public class ServiceCatalogResponseDto
{
    /// List of active packages.
    public IReadOnlyList<TestPackageDto> Packages { get; set; } = Array.Empty<TestPackageDto>();

    /// Distinct category list (for frontend filter chips).
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();

    /// Total number of packages returned.
    public int TotalCount { get; set; }
}