using BMS.Application.DTOs;
using BMS.Application.DTOs.Common;

namespace BMS.Application.Interfaces;

/// Service contract for UC01 – View Service Catalog &amp; Search Test Packages.
/// Role: All (public read for patients and staff; staff with MANAGE_SERVICE_PACKAGE can also filter inactive).
public interface ITestPackageService
{
    /// <summary>
    /// Retrieves all active laboratory test packages.
    /// Optionally filters by category or searches by name/code.
    /// </summary>
    Task<ServiceCatalogResponseDto> GetServiceCatalogAsync(
        string? category = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches and filters test packages with keyword matching, price range, category, sample tube, sorting, and pagination.
    /// </summary>
    Task<PagedResult<TestPackageDto>> SearchTestPackagesAsync(
        TestPackageSearchRequest request,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single package by its Id or PackageCode.
    /// </summary>
    Task<TestPackageDto?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single package by its Id or PackageCode with optional inactive visibility.
    /// </summary>
    Task<TestPackageDto?> GetByIdAsync(
        string id,
        bool includeInactive,
        CancellationToken cancellationToken = default);
}