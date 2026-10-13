namespace BMS.Application.DTOs;

/// DTO returned to clients when viewing the service catalog.
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