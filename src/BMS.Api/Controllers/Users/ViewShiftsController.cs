using BMS.Core.Constants;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.Shifts;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.ViewShifts.Controllers;

/// <summary>
/// UC36 – View Shifts (read-only).
/// Thin facade over existing <see cref="IShiftService.ListShiftsAsync"/>.
/// Production app already exposes the same data via GET /api/shifts (ShiftsController).
/// </summary>
[ApiController]
[Route("api/view-shifts")]
[Produces("application/json")]
// WHY: UC36: all staff. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleSets.ShiftViewers)]
public class ViewShiftsController(IShiftService shifts) : ControllerBase
{
    /// <summary>
    /// List shifts in a date range (optional specialty / department filter).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ShiftDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ShiftDto>>> List(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? specialty,
        [FromQuery] string? department,
        [FromQuery] string? doctorId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var fromDate = (from ?? DateTime.UtcNow.Date).Date;
        var toDate = (to ?? fromDate.AddDays(7)).Date;

        var filter = new ShiftFilterRequest
        {
            FromDate = fromDate,
            ToDate = toDate,
            DoctorId = doctorId,
            EmployeeId = doctorId,
            Department = department ?? specialty,
            Status = status,
            Page = page,
            PageSize = pageSize
        };

        var result = await shifts.ListShiftsAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>Shifts for a single calendar day.</summary>
    [HttpGet("day")]
    [ProducesResponseType(typeof(PagedResult<ShiftDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ShiftDto>>> ByDay(
        [FromQuery] DateTime? date,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var day = (date ?? DateTime.UtcNow.Date).Date;
        var filter = new ShiftFilterRequest
        {
            FromDate = day,
            ToDate = day,
            Page = page,
            PageSize = pageSize
        };
        var result = await shifts.ListShiftsAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>Shift detail (same as GET /api/shifts/{id}).</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ShiftDto>> GetById(string id, CancellationToken ct)
    {
        var result = await shifts.GetShiftByIdAsync(id, ct);
        return Ok(result);
    }
}
