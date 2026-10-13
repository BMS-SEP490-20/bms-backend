using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

/// Repository contract for the <c>test_packages</c> collection.
public interface ITestPackageRepository
{
    /// Returns all active packages, optionally filtered by category and search keyword.
    Task<IReadOnlyList<TestPackage>> GetActivePackagesAsync(
        string? category = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// Returns a single package by Id (only if is_active = true).
    Task<TestPackage?> GetActiveByIdAsync(string id, CancellationToken cancellationToken = default);
}