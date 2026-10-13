using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Data repository contract for clinical laboratory test criteria master data (UC18).
/// Conforms to: Repository Pattern & ISO 15189 Quality Standards.
/// </summary>
public interface ITestCriterionRepository
{
    Task<(List<TestCriterionEntity> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search = null,
        string? category = null,
        bool? isActive = null,
        CancellationToken ct = default);

    Task<TestCriterionEntity?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<TestCriterionEntity?> GetByCodeAsync(string code, CancellationToken ct = default);

    Task CreateAsync(TestCriterionEntity entity, CancellationToken ct = default);

    Task UpdateAsync(TestCriterionEntity entity, CancellationToken ct = default);

    Task DeactivateAsync(string id, CancellationToken ct = default);

    Task EnsureDefaultCriteriaSeededAsync(CancellationToken ct = default);
}
