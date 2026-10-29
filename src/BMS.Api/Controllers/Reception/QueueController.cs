using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using BMS.Application.DTOs.Reception;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Api.Controllers.Reception;

/// <summary>
/// UC27 – Assign Queue Number & Reception Queue Board API.
/// Endpoints utilized by Reception Desk, Kiosk Ticketing, and Queue Calling screens.
/// </summary>
[ApiController]
[Route("api/reception/queue")]
// WHY: UC27: Receptionist. Live board requires login until Phát approves a public masked board (C3). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleNames.Receptionist)]
public class QueueController : ControllerBase
{
    private readonly IQueueService _queueService;
    private readonly IPatientReceptionService _receptionService;

    public QueueController(IQueueService queueService, IPatientReceptionService receptionService)
    {
        _queueService = queueService;
        _receptionService = receptionService;
    }

    /// <summary>
    /// UC27 – Retrieves the live queue board, current waiting tickets, and queue statistics.
    /// When <paramref name="roomId"/> or <paramref name="doctorId"/> is given, returns the clinic queue status
    /// (waiting list + currently serving, I2-025) instead.
    /// </summary>
    /// <remarks>
    /// GET api/reception/queue used to be declared by both this controller and PatientReceptionController
    /// (ambiguous route at runtime). Both behaviours are kept here; the clinic status is also available
    /// unambiguously at GET api/reception/queue-status.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(QueueBoardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(QueueStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLiveQueue(
        [FromQuery] string? roomId,
        [FromQuery] string? doctorId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(roomId) || !string.IsNullOrWhiteSpace(doctorId))
        {
            return Ok(await _receptionService.GetQueueStatusAsync(roomId, doctorId, ct));
        }

        var result = await _queueService.GetLiveQueueBoardAsync(ct);
        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// UC27 – Issues the next sequential queue ticket.
    /// </summary>
    [HttpPost("issue")]
    [ProducesResponseType(typeof(QueueTicketDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IssueTicket([FromBody] IssueTicketRequestDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PatientName) && string.IsNullOrWhiteSpace(request.PatientId))
        {
            return BadRequest(new
            {
                success = false,
                message = "Vui lòng cung cấp thông tin bệnh nhân (PatientName hoặc PatientId)."
            });
        }

        var result = await _queueService.IssueTicketAsync(request, ct);
        return CreatedAtAction(nameof(GetLiveQueue), new { id = result.Id }, new
        {
            success = true,
            message = $"Đã cấp số thứ tự {result.TicketNumber} thành công.",
            data = result
        });
    }

    /// <summary>
    /// UC27/UC28 – Calls the next waiting patient to the counter: STAT first, then policy priority and routine
    /// alternate 1:1, first come first served within each group (BA:L33).
    /// </summary>
    [HttpPost("call-next")]
    [ProducesResponseType(typeof(QueueTicketDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CallNext([FromBody] CallTicketRequestDto request, CancellationToken ct)
    {
        var called = await _queueService.CallNextAsync(request.CounterName, ct);
        return called == null ? NoContent() : Ok(new { success = true, data = called });
    }

    /// <summary>
    /// UC27 – Calls a ticket to an active collection counter.
    /// </summary>
    [HttpPut("{id}/call")]
    public async Task<IActionResult> CallTicket(
        string id,
        [FromBody] CallTicketRequestDto request,
        CancellationToken ct)
    {
        var success = await _queueService.CallTicketAsync(id, request.CounterName, ct);
        if (!success)
        {
            return NotFound(new { success = false, message = "Không tìm thấy số thứ tự này." });
        }

        return Ok(new
        {
            success = true,
            message = $"Đã gọi số thứ tự vào {request.CounterName} thành công."
        });
    }

    /// <summary>
    /// UC27 – Updates the lifecycle status of a ticket (Collecting, Completed, Skipped).
    /// </summary>
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(
        string id,
        [FromBody] UpdateTicketStatusRequestDto request,
        CancellationToken ct)
    {
        var success = await _queueService.UpdateTicketStatusAsync(id, request, ct);
        if (!success)
        {
            return NotFound(new { success = false, message = "Không tìm thấy số thứ tự cần cập nhật." });
        }

        return Ok(new
        {
            success = true,
            message = $"Cập nhật trạng thái số thứ tự sang '{request.Status}' thành công."
        });
    }
}
