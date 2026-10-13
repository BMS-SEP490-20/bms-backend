using BMS.Application.DTOs.Roles;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service contract defining RBAC business validation and CRUD lifecycle operations (UC13).
/// Enforces rules BR-ROLE-01 (System Role Lock) and BR-ROLE-02 (User Association Check).
/// </summary>
public interface IRoleService
{
    Task<PagedResult<RoleResponseDto>> GetRolesPagedAsync(RoleQueryParameters parameters, CancellationToken ct = default);

    Task<RoleResponseDto?> GetRoleByIdAsync(string id, CancellationToken ct = default);

    Task<(bool Success, string Message, RoleResponseDto? Data)> CreateRoleAsync(CreateRoleDto dto, CancellationToken ct = default);

    Task<(bool Success, string Message, RoleResponseDto? Data)> UpdateRoleAsync(string id, UpdateRoleDto dto, CancellationToken ct = default);

    Task<(bool Success, string Message)> DeleteRoleAsync(string id, CancellationToken ct = default);

    Dictionary<string, List<string>> GetPermissionMatrix();
}
