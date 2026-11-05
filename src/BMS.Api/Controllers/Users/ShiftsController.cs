using BMS.Core.Constants;
using BMS.Core.Exceptions;
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
        try
        {
            var result = await shifts.ListShiftsAsync(filter, ct);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                title = ex.Code,
                code = ex.Code,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = 500,
                title = "INTERNAL_SERVER_ERROR",
                code = "INTERNAL_SERVER_ERROR",
                detail = "Không thể tải danh sách ca làm việc lúc này. Vui lòng thử lại sau.",
                message = "Không thể tải danh sách ca làm việc lúc này. Vui lòng thử lại sau."
            });
        }
    }

    /// <summary>
    /// Retrieves all active doctor and lab rooms for shift scheduling.
    /// </summary>
    [Authorize(Roles = RoleSets.ShiftViewers)]
    [HttpGet("rooms")]
    [ProducesResponseType(typeof(IReadOnlyList<ShiftRoomDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShiftRoomDto>>> GetRooms(CancellationToken ct)
    {
        try
        {
            var result = await shifts.GetRoomsAsync(ct);
            return Ok(new { success = true, data = result });
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                title = ex.Code,
                code = ex.Code,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = 500,
                title = "INTERNAL_SERVER_ERROR",
                code = "INTERNAL_SERVER_ERROR",
                detail = "Không thể tải danh sách phòng trực từ cơ sở dữ liệu.",
                message = "Không thể tải danh sách phòng trực từ cơ sở dữ liệu."
            });
        }
    }

    /// <summary>
    /// Retrieves all active clinical &amp; laboratory staff accounts for shift scheduling (UC35).
    /// </summary>
    [Authorize(Roles = RoleSets.ShiftViewers)]
    [HttpGet("staff")]
    [ProducesResponseType(typeof(IReadOnlyList<ShiftStaffDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShiftStaffDto>>> GetStaff(CancellationToken ct)
    {
        try
        {
            var result = await shifts.GetStaffAsync(ct);
            return Ok(new { success = true, data = result });
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                title = ex.Code,
                code = ex.Code,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = 500,
                title = "INTERNAL_SERVER_ERROR",
                code = "INTERNAL_SERVER_ERROR",
                detail = "Không thể tải danh sách nhân sự phân ca từ cơ sở dữ liệu.",
                message = "Không thể tải danh sách nhân sự phân ca từ cơ sở dữ liệu."
            });
        }
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
        try
        {
            var result = await shifts.GetShiftByIdAsync(id, ct);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                title = ex.Code,
                code = ex.Code,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = 500,
                title = "INTERNAL_SERVER_ERROR",
                code = "INTERNAL_SERVER_ERROR",
                detail = "Không thể tải thông tin chi tiết ca làm việc.",
                message = "Không thể tải thông tin chi tiết ca làm việc."
            });
        }
    }

    /// <summary>
    /// Creates a new clinical or laboratory work shift with working time validation and employee/room conflict detection (Create Shifts).
    /// </summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpPost]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShiftDto>> CreateShift(
        [FromBody] CreateShiftRequest request,
        CancellationToken ct)
    {
        try
        {
            var created = await shifts.CreateShiftAsync(request, CurrentUserId, ct);
            return CreatedAtAction(nameof(GetShiftById), new { id = created.Id }, created);
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                title = ex.Code,
                code = ex.Code,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = 500,
                title = "INTERNAL_SERVER_ERROR",
                code = "INTERNAL_SERVER_ERROR",
                detail = "Không thể tạo ca làm việc mới lúc này.",
                message = "Không thể tạo ca làm việc mới lúc này."
            });
        }
    }

    /// <summary>
    /// Updates an existing work shift with working time validation, capacity checks, and conflict detection (Update Shifts).
    /// </summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
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
        try
        {
            var updated = await shifts.UpdateShiftAsync(id, request, CurrentUserId, ct);
            return Ok(updated);
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                title = ex.Code,
                code = ex.Code,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = 500,
                title = "INTERNAL_SERVER_ERROR",
                code = "INTERNAL_SERVER_ERROR",
                detail = "Không thể cập nhật thông tin ca làm việc lúc này.",
                message = "Không thể cập nhật thông tin ca làm việc lúc này."
            });
        }
    }

    /// <summary>
    /// Deletes a work shift after verifying no active patient appointments or bookings depend on it (Delete Shifts).
    /// </summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteShift(
        string id,
        CancellationToken ct)
    {
        try
        {
            await shifts.DeleteShiftAsync(id, CurrentUserId, ct);
            return NoContent();
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                title = ex.Code,
                code = ex.Code,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = 500,
                title = "INTERNAL_SERVER_ERROR",
                code = "INTERNAL_SERVER_ERROR",
                detail = "Không thể xoá ca làm việc lúc này.",
                message = "Không thể xoá ca làm việc lúc này."
            });
        }
    }

    private string? CurrentUserId =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
}
