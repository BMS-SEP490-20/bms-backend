using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using BMS.Core.Entities;

namespace BMS.Application.Services;

/// Implementation of <see cref="ITestPackageService"/> – UC01 View Service Catalog.
public class TestPackageService : ITestPackageService
{
    private readonly ITestPackageRepository _repository;

    /// Initializes the service with its repository dependency.
    public TestPackageService(ITestPackageRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public async Task<ServiceCatalogResponseDto> GetServiceCatalogAsync(
        string? category = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var packages = await _repository.GetActivePackagesAsync(category, search, cancellationToken);

        var dtos = packages.Select(MapToDto).ToList();

        // Load categories from the full active set (independent of current filters)
        // so the frontend always has a complete filter list.
        var allActive = string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(search)
            ? packages
            : await _repository.GetActivePackagesAsync(null, null, cancellationToken);

        var categories = allActive
            .Select(p => p.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();

        return new ServiceCatalogResponseDto
        {
            Packages = dtos,
            Categories = categories,
            TotalCount = dtos.Count
        };
    }

    /// <inheritdoc />
    public async Task<TestPackageDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var entity = await _repository.GetActiveByIdAsync(id, cancellationToken);
        return entity is null ? null : MapToDto(entity);
    }

    /// Maps an entity to its DTO representation.
    private static TestPackageDto MapToDto(TestPackage entity) => new()
    {
        Id = entity.Id,
        PackageCode = entity.PackageCode,
        PackageName = entity.PackageName,
        Category = entity.Category,
        Price = entity.Price,
        Description = entity.Description,
        SampleTube = entity.SampleTube,
        SampleVolumeMl = entity.SampleVolumeMl,
        IsActive = entity.IsActive
    };
}