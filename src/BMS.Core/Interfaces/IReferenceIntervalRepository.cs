using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for laboratory reference interval domain entities (UC57).
/// </summary>
public interface IReferenceIntervalRepository
{
    Task<(List<ReferenceIntervalEntity> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? criterionCode = null,
        string? gender = null,
        bool? isActive = null,
        CancellationToken ct = default);

    Task<List<ReferenceIntervalEntity>> GetByCriterionCodeAsync(
        string criterionCode,
        bool activeOnly = true,
        CancellationToken ct = default);

    Task<List<ReferenceIntervalEntity>> GetByCriterionIdAsync(
        string criterionId,
        bool activeOnly = true,
        CancellationToken ct = default);

    Task<ReferenceIntervalEntity?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<ReferenceIntervalEntity> CreateAsync(ReferenceIntervalEntity entity, CancellationToken ct = default);

    Task<ReferenceIntervalEntity?> UpdateAsync(ReferenceIntervalEntity entity, CancellationToken ct = default);

    Task<bool> DeactivateAsync(string id, CancellationToken ct = default);

    Task EnsureDefaultIntervalsSeededAsync(CancellationToken ct = default);
}
