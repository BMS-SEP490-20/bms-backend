using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Data repository contract for Role-Based Access Control (RBAC) persistence (UC13).
/// Conforms to: Repository Pattern & Clean Architecture.
/// </summary>
public interface IRoleRepository
{
    Task<(List<RoleEntity> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search = null,
        bool? isActive = null,
        CancellationToken ct = default);

    Task<RoleEntity?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<RoleEntity?> GetByCodeAsync(string roleCode, CancellationToken ct = default);

    Task CreateAsync(RoleEntity role, CancellationToken ct = default);

    Task UpdateAsync(RoleEntity role, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    Task EnsureDefaultRolesSeededAsync(CancellationToken ct = default);
}
