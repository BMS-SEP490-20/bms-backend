using BMS.Application.DTOs;
using BMS.Application.DTOs.Common;
using BMS.Application.Interfaces;
using BMS.Core.Entities;

namespace BMS.Application.Services;

/// <summary>
/// Implementation of <see cref="ITestPackageService"/> – UC01 View Service Catalog &amp; Search Test Packages.
/// </summary>
public class TestPackageService : ITestPackageService
{
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
        { "name", "code", "price", "category" };

    private readonly ITestPackageRepository _repository;

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
        var criteriaMap = await _repository.GetCriteriaByPackageIdsAsync(packages.Select(p => p.Id), cancellationToken);

        var dtos = packages.Select(p => MapToDto(p, criteriaMap)).ToList();

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
    /// <summary>
    /// UC20 - Search Test Packages (Issue I2-001 | BR-062).
    /// Provides multi-field keyword matching across package name, code, description, and included test criteria.
    /// Supports pagination, price range filters, and category sorting.
    /// </summary>
    public async Task<PagedResult<TestPackageDto>> SearchTestPackagesAsync(
        TestPackageSearchRequest request,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var sortBy = !string.IsNullOrWhiteSpace(request.SortBy) && AllowedSortFields.Contains(request.SortBy.Trim())
            ? AllowedSortFields.First(f => f.Equals(request.SortBy.Trim(), StringComparison.OrdinalIgnoreCase))
            : "category";

        var sortDesc = !string.IsNullOrWhiteSpace(request.SortDir) &&
                       request.SortDir.Trim().Equals("desc", StringComparison.OrdinalIgnoreCase);

        var query = new TestPackageSearchQuery(
            Keyword: request.EffectiveKeyword,
            Category: string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim(),
            SampleTube: string.IsNullOrWhiteSpace(request.SampleTube) ? null : request.SampleTube.Trim(),
            MinPrice: request.MinPrice,
            MaxPrice: request.MaxPrice,
            IsActive: includeInactive ? request.IsActive : true,
            SortBy: sortBy,
            SortDesc: sortDesc,
            Page: page,
            PageSize: pageSize);

        var paged = await _repository.SearchPackagesAsync(query, cancellationToken);
        var criteriaMap = await _repository.GetCriteriaByPackageIdsAsync(paged.Items.Select(p => p.Id), cancellationToken);

        var items = paged.Items.Select(p => MapToDto(p, criteriaMap)).ToList();
        return new PagedResult<TestPackageDto>(items, paged.Page, paged.PageSize, paged.TotalItems);
    }

    /// <inheritdoc />
    public Task<TestPackageDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetByIdAsync(id, includeInactive: false, cancellationToken);

    /// <inheritdoc />
    public async Task<TestPackageDto?> GetByIdAsync(
        string id,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var entity = await _repository.GetByIdOrCodeAsync(id, includeInactive, cancellationToken);
        if (entity is null)
            return null;

        var criteriaMap = await _repository.GetCriteriaByPackageIdsAsync([entity.Id], cancellationToken);
        return MapToDto(entity, criteriaMap);
    }

    private static TestPackageDto MapToDto(
        TestPackage entity,
        IReadOnlyDictionary<string, IReadOnlyList<TestPackageCriterionSummaryDto>> criteriaMap)
    {
        criteriaMap.TryGetValue(entity.Id, out var criteria);
        return new TestPackageDto
        {
            Id = entity.Id,
            PackageCode = entity.PackageCode,
            PackageName = entity.PackageName,
            Category = entity.Category,
            Price = entity.Price,
            Description = entity.Description,
            SampleTube = entity.SampleTube,
            SampleVolumeMl = entity.SampleVolumeMl,
            IsActive = entity.IsActive,
            Criteria = criteria ?? Array.Empty<TestPackageCriterionSummaryDto>()
        };
    }
}