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
public class QueueController : ControllerBase
{
    private readonly IQueueService _queueService;

    public QueueController(IQueueService queueService)
    {
        _queueService = queueService;
    }

    /// <summary>
    /// UC27 – Retrieves the live queue board, current waiting tickets, and queue statistics.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(QueueBoardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLiveQueue(CancellationToken ct)
    {
        var result = await _queueService.GetLiveQueueBoardAsync(ct);
        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// UC27 – Issues the next sequential queue ticket (e.g., Q-019).
    /// </summary>
    [HttpPost("issue")]
    [ProducesResponseType(typeof(QueueTicketDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> IssueTicket([FromBody] IssueTicketRequestDto request, CancellationToken ct)
    {
        var result = await _queueService.IssueTicketAsync(request, ct);
        return CreatedAtAction(nameof(GetLiveQueue), new { id = result.Id }, new
        {
            success = true,
            message = $"Đã cấp số thứ tự {result.TicketNumber} thành công.",
            data = result
        });
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
