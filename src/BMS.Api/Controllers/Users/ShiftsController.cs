using BMS.Core.Constants;
using System.Security.Claims;
using BMS.Api.Authorization;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.Shifts;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Users;

/// <summary>
/// Controller scheduling clinical &amp; laboratory staff duty shifts
/// (View Shifts, Create Shifts, Update Shifts, Delete Shifts use cases).
/// </summary>
[ApiController]
[Route("api/shifts")]
[Route("api/v1/shifts")]
[Produces("application/json")]
// WHY: UC35: view all staff; create/edit/delete Manager. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class ShiftsController(IShiftService shifts) : ControllerBase
{
    /// <summary>
    /// Retrieves shift assignments with working time, employee details, room info, filtering, and pagination (View Shifts).
    /// </summary>
    [Authorize(Roles = RoleSets.ShiftViewers)]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ShiftDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ShiftDto>>> GetShifts(
        [FromQuery] ShiftFilterRequest filter,
        CancellationToken ct)
    {
        var result = await shifts.ListShiftsAsync(filter, ct);
        return Ok(result);
    }

    
    /// <summary>
    /// Retrieves all active doctor and lab rooms for shift scheduling.
    /// </summary>
    [Authorize(Roles = RoleSets.ShiftViewers)]
    [HttpGet("rooms")]
    [ProducesResponseType(typeof(IReadOnlyList<ShiftRoomDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShiftRoomDto>>> GetRooms(CancellationToken ct)
    {
        var result = await shifts.GetRoomsAsync(ct);
        return Ok(new { success = true, data = result });
    }

/// <summary>
    /// Retrieves details of a specific work shift by ID (View Shifts).
    /// </summary>
    [Authorize(Roles = RoleSets.ShiftViewers)]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShiftDto>> GetShiftById(
        string id,
        CancellationToken ct)
    {
        var result = await shifts.GetShiftByIdAsync(id, ct);
        return Ok(result);
    }

    /// <summary>
    /// Creates a new clinical or laboratory work shift with working time validation and employee/room conflict detection (Create Shifts).
    /// </summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpPost]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShiftDto>> CreateShift(
        [FromBody] CreateShiftRequest request,
        CancellationToken ct)
    {
        var created = await shifts.CreateShiftAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(GetShiftById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Updates an existing work shift with working time validation, capacity checks, and conflict detection (Update Shifts).
    /// </summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShiftDto>> UpdateShift(
        string id,
        [FromBody] UpdateShiftRequest request,
        CancellationToken ct)
    {
        var updated = await shifts.UpdateShiftAsync(id, request, CurrentUserId, ct);
        return Ok(updated);
    }

    /// <summary>
    /// Deletes a work shift after verifying no active patient appointments or bookings depend on it (Delete Shifts).
    /// </summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteShift(
        string id,
        CancellationToken ct)
    {
        await shifts.DeleteShiftAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    private string? CurrentUserId =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
}
