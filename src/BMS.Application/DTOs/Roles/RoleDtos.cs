using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Roles;

/// <summary>
/// Query parameters for paginated and filtered role searches.
/// </summary>
public class RoleQueryParameters
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// Payload to create a new custom role in the system (UC13).
/// </summary>
public class CreateRoleDto
{
    [Required(ErrorMessage = "RoleCode is required.")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "RoleCode must be between 3 and 30 characters.")]
    [RegularExpression(@"^[A-Z0-9_]+$", ErrorMessage = "RoleCode must consist of uppercase alphanumeric characters and underscores only.")]
    public string RoleCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "RoleName is required.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "RoleName must be between 3 and 100 characters.")]
    public string RoleName { get; set; } = string.Empty;

    [StringLength(250, ErrorMessage = "Description cannot exceed 250 characters.")]
    public string Description { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = new();

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Payload to update an existing role's name, description, active status, and permission matrix (UC13).
/// </summary>
public class UpdateRoleDto
{
    [Required(ErrorMessage = "RoleName is required.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "RoleName must be between 3 and 100 characters.")]
    public string RoleName { get; set; } = string.Empty;

    [StringLength(250, ErrorMessage = "Description cannot exceed 250 characters.")]
    public string Description { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = new();

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Sanitized role view response returned to clients.
/// </summary>
public class RoleResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; }
    public long UserCount { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>
/// Generic paginated list response wrapper.
/// </summary>
/// <typeparam name="T">Item entity type.</typeparam>
public class PagedResult<T>
{
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public long TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public List<T> Items { get; set; } = new();
}
