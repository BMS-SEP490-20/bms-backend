using BMS.Application.DTOs.Roles;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>
/// Business service implementing RBAC role lifecycle management and security constraints (UC13).
/// Enforces invariants:
/// - BR-ROLE-01: Built-in clinical system roles cannot be deleted or renamed.
/// - BR-ROLE-02: Roles currently associated with active users cannot be removed.
/// </summary>
public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;

    public RoleService(IRoleRepository roleRepository, IUserRepository userRepository)
    {
        _roleRepository = roleRepository;
        _userRepository = userRepository;
    }

    /// <summary>
    /// Retrieves paginated list of roles with live user count per role.
    /// </summary>
    public async Task<PagedResult<RoleResponseDto>> GetRolesPagedAsync(
        RoleQueryParameters parameters,
        CancellationToken ct = default)
    {
        await _roleRepository.EnsureDefaultRolesSeededAsync(ct);

        var (items, total) = await _roleRepository.GetPagedAsync(
            parameters.Page,
            parameters.PageSize,
            parameters.Search,
            parameters.IsActive,
            ct);

        var responseItems = new List<RoleResponseDto>();
        foreach (var role in items)
        {
            var userCount = await _userRepository.CountUsersByRoleIdAsync(role.Id, ct);
            responseItems.Add(MapToDto(role, userCount));
        }

        return new PagedResult<RoleResponseDto>
        {
            CurrentPage = parameters.Page,
            PageSize = parameters.PageSize,
            TotalCount = total,
            Items = responseItems
        };
    }

    /// <summary>
    /// Retrieves role details by identifier.
    /// </summary>
    public async Task<RoleResponseDto?> GetRoleByIdAsync(string id, CancellationToken ct = default)
    {
        var role = await _roleRepository.GetByIdAsync(id, ct);
        if (role == null) return null;

        var userCount = await _userRepository.CountUsersByRoleIdAsync(role.Id, ct);
        return MapToDto(role, userCount);
    }

    /// <summary>
    /// Creates a new custom role with validated permission matrix.
    /// </summary>
    public async Task<(bool Success, string Message, RoleResponseDto? Data)> CreateRoleAsync(
        CreateRoleDto dto,
        CancellationToken ct = default)
    {
        var normalizedCode = dto.RoleCode.Trim().ToUpperInvariant();

        // Check duplicate code
        var existing = await _roleRepository.GetByCodeAsync(normalizedCode, ct);
        if (existing != null)
        {
            return (false, $"Role code '{normalizedCode}' already exists in the system.", null);
        }

        var newRole = new RoleEntity
        {
            RoleCode = normalizedCode,
            RoleName = dto.RoleName.Trim(),
            Description = dto.Description.Trim(),
            Permissions = dto.Permissions.Distinct().ToList(),
            IsSystemRole = false,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _roleRepository.CreateAsync(newRole, ct);
        return (true, "Role created successfully.", MapToDto(newRole, 0));
    }

    /// <summary>
    /// Updates existing role metadata and permissions.
    /// </summary>
    public async Task<(bool Success, string Message, RoleResponseDto? Data)> UpdateRoleAsync(
        string id,
        UpdateRoleDto dto,
        CancellationToken ct = default)
    {
        var role = await _roleRepository.GetByIdAsync(id, ct);
        if (role == null)
        {
            return (false, "Role not found.", null);
        }

        role.RoleName = dto.RoleName.Trim();
        role.Description = dto.Description.Trim();
        role.Permissions = dto.Permissions.Distinct().ToList();
        role.IsActive = dto.IsActive;
        role.UpdatedAt = DateTime.UtcNow;

        await _roleRepository.UpdateAsync(role, ct);
        var userCount = await _userRepository.CountUsersByRoleIdAsync(role.Id, ct);
        return (true, "Role updated successfully.", MapToDto(role, userCount));
    }

    /// <summary>
    /// Soft deletes a custom role after verifying BR-ROLE-01 and BR-ROLE-02 safety constraints.
    /// </summary>
    public async Task<(bool Success, string Message)> DeleteRoleAsync(string id, CancellationToken ct = default)
    {
        var role = await _roleRepository.GetByIdAsync(id, ct);
        if (role == null)
        {
            return (false, "Role not found.");
        }

        // Rule BR-ROLE-01: Lock system roles
        if (role.IsSystemRole)
        {
            return (false, "Core system roles (Admin, Doctor, etc.) cannot be deleted per business rule BR-ROLE-01.");
        }

        // Rule BR-ROLE-02: Prevent deletion if users are assigned
        var userCount = await _userRepository.CountUsersByRoleIdAsync(role.Id, ct);
        if (userCount > 0)
        {
            return (false, $"Cannot delete role because {userCount} active user(s) are currently assigned to it (BR-ROLE-02).");
        }

        await _roleRepository.DeleteAsync(id, ct);
        return (true, "Role deleted successfully.");
    }

    /// <summary>
    /// Returns the global tree of available system permissions.
    /// </summary>
    public Dictionary<string, List<string>> GetPermissionMatrix()
    {
        return SystemPermissions.PermissionMatrix;
    }

    private static RoleResponseDto MapToDto(RoleEntity role, long userCount)
    {
        return new RoleResponseDto
        {
            Id = role.Id,
            RoleCode = role.RoleCode,
            RoleName = role.RoleName,
            Description = role.Description,
            Permissions = role.Permissions,
            IsSystemRole = role.IsSystemRole,
            IsActive = role.IsActive,
            UserCount = userCount,
            CreatedAt = role.CreatedAt.ToString("dd/MM/yyyy"),
            UpdatedAt = role.UpdatedAt.ToString("dd/MM/yyyy")
        };
    }
}
