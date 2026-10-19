using BMS.Schedule.DTOs;
using BMS.Schedule.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Schedule.Controllers;

/// <summary>UC31 – Manage Personal Schedule (CRUD doctor_shifts).</summary>
[ApiController]
[Route("api/schedules")]
[Authorize]
public class ScheduleController : ControllerBase
{
    private readonly IScheduleService _service;

    public ScheduleController(IScheduleService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string doctorId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(doctorId))
            return BadRequest(new ApiErrorResponse { Code = "MSG-ERR-VAL-01", Message = "doctorId is required." });
        var list = await _service.ListAsync(doctorId, from, to, ct);
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var item = await _service.GetByIdAsync(id, ct);
        return item == null
            ? NotFound(new ApiErrorResponse { Code = "MSG-ERR-SCHED-03", Message = "Không tìm thấy ca." })
            : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateShiftRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ApiErrorResponse { Code = "MSG-ERR-VAL-01", Message = "Invalid data." });
        try
        {
            var created = await _service.CreateAsync(request, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse { Code = Extract(ex.Message), Message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateShiftRequest request, CancellationToken ct)
    {
        try
        {
            var updated = await _service.UpdateAsync(id, request, ct);
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse { Code = Extract(ex.Message), Message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse { Code = Extract(ex.Message), Message = ex.Message });
        }
    }

    private static string Extract(string m)
    {
        var i = m.IndexOf(':');
        return i > 0 ? m[..i].Trim() : "MSG-ERR-SCHED-00";
    }
}
