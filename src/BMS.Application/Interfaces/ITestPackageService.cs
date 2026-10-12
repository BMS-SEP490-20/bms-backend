using BMS.Application.DTOs;

namespace BMS.Application.Interfaces;

/// Service contract for UC01 – View Service Catalog.
/// Role: All (public read for patients and staff).
public interface ITestPackageService
{
    /// <summary>
    /// Retrieves all active laboratory test packages.
    /// Optionally filters by category or searches by name/code.
    /// </summary>
    /// <param name="category">Filter by category group (null = all).</param>
    /// <param name="search">Keyword to search in package name or code (null = no filter).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>ServiceCatalogResponseDto containing packages and categories.</returns>
    Task<ServiceCatalogResponseDto> GetServiceCatalogAsync(
        string? category = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// Retrieves a single package by its Id.
    /// <param name="id">Package Id (PKG-xxx).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>TestPackageDto or null if not found / inactive.</returns>
    Task<TestPackageDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
}