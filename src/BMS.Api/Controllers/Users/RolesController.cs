using BMS.Application.DTOs.Roles;
using BMS.Application.DTOs.Users;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Users;

/// <summary>
/// Controller providing full CRUD management for RBAC roles and permissions (UC13).
/// Conforms to: NIST RBAC Standard, OWASP Least Privilege & RESTful Design.
/// </summary>
[ApiController]
[Route("api/v1/roles")]
[Produces("application/json")]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;
    private readonly ILogger<RolesController> _logger;

    public RolesController(IRoleService roleService, ILogger<RolesController> logger)
    {
        _roleService = roleService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves paginated list of system and custom roles with search and status filters (UC13).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RoleResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles([FromQuery] RoleQueryParameters parameters, CancellationToken ct = default)
    {
        var result = await _roleService.GetRolesPagedAsync(parameters, ct);
        return Ok(ApiResponse<PagedResult<RoleResponseDto>>.Ok(result, "Roles retrieved successfully."));
    }

    /// <summary>
    /// Retrieves full role details and assigned permission matrix by identifier.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<RoleResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRoleById(string id, CancellationToken ct = default)
    {
        var role = await _roleService.GetRoleByIdAsync(id, ct);
        if (role == null)
        {
            return NotFound(ApiResponse<object>.Fail("Role not found.", "MSG-ERR-ROLE-NOT-FOUND"));
        }

        return Ok(ApiResponse<RoleResponseDto>.Ok(role, "Role loaded successfully."));
    }

    /// <summary>
    /// Returns the global tree of available system permissions grouped by module.
    /// </summary>
    [HttpGet("permissions/matrix")]
    [ProducesResponseType(typeof(ApiResponse<Dictionary<string, List<string>>>), StatusCodes.Status200OK)]
    public IActionResult GetPermissionMatrix()
    {
        var matrix = _roleService.GetPermissionMatrix();
        return Ok(ApiResponse<Dictionary<string, List<string>>>.Ok(matrix, "Permission matrix retrieved successfully."));
    }

    /// <summary>
    /// Creates a new custom clinical or administrative role (UC13).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<RoleResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<object>.Fail("Invalid role payload.", "MSG-ERR-VALIDATION"));
        }

        _logger.LogInformation("[BMS Roles API] Creating role with code {RoleCode}", dto.RoleCode);
        var (success, message, data) = await _roleService.CreateRoleAsync(dto, ct);

        if (!success)
        {
            return Conflict(ApiResponse<object>.Fail(message, "MSG-ERR-ROLE-EXISTS"));
        }

        return CreatedAtAction(nameof(GetRoleById), new { id = data!.Id }, ApiResponse<RoleResponseDto>.Ok(data, message));
    }

    /// <summary>
    /// Updates role name, description, active state, and assigned permission matrix (UC13).
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<RoleResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] UpdateRoleDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<object>.Fail("Invalid update payload.", "MSG-ERR-VALIDATION"));
        }

        _logger.LogInformation("[BMS Roles API] Updating role identifier {RoleId}", id);
        var (success, message, data) = await _roleService.UpdateRoleAsync(id, dto, ct);

        if (!success)
        {
            return BadRequest(ApiResponse<object>.Fail(message, "MSG-ERR-ROLE-UPDATE-FAILED"));
        }

        return Ok(ApiResponse<RoleResponseDto>.Ok(data!, message));
    }

    /// <summary>
    /// Deactivates / soft deletes a role with strict business rule checks (BR-ROLE-01 & BR-ROLE-02).
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteRole(string id, CancellationToken ct = default)
    {
        _logger.LogInformation("[BMS Roles API] Attempting to remove role identifier {RoleId}", id);
        var (success, message) = await _roleService.DeleteRoleAsync(id, ct);

        if (!success)
        {
            return BadRequest(ApiResponse<object>.Fail(message, "MSG-ERR-ROLE-DELETE-BLOCKED"));
        }

        return Ok(ApiResponse<object>.Ok(new { Id = id }, message));
    }
}
