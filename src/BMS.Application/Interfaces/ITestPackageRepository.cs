using BMS.Application.DTOs;
using BMS.Application.DTOs.Common;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

public sealed record TestPackageSearchQuery(
    string? Keyword,
    string? Category,
    string? SampleTube,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool? IsActive,
    string SortBy,
    bool SortDesc,
    int Page,
    int PageSize);

/// Repository contract for the <c>test_packages</c> collection.
public interface ITestPackageRepository
{
    /// Returns all active packages, optionally filtered by category and search keyword.
    Task<IReadOnlyList<TestPackage>> GetActivePackagesAsync(
        string? category = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// Returns a single package by Id or PackageCode (only if is_active = true unless includeInactive = true).
    Task<TestPackage?> GetActiveByIdAsync(string id, CancellationToken cancellationToken = default);

    /// Returns a single package by Id or PackageCode with optional inactive visibility.
    Task<TestPackage?> GetByIdOrCodeAsync(string idOrCode, bool includeInactive = false, CancellationToken cancellationToken = default);

    /// Searches and filters test packages with pagination and sorting.
    Task<PagedResult<TestPackage>> SearchPackagesAsync(
        TestPackageSearchQuery query,
        CancellationToken cancellationToken = default);

    /// Retrieves mapped test criteria for a set of test package IDs.
    Task<IReadOnlyDictionary<string, IReadOnlyList<TestPackageCriterionSummaryDto>>> GetCriteriaByPackageIdsAsync(
        IEnumerable<string> packageIds,
        CancellationToken cancellationToken = default);
}