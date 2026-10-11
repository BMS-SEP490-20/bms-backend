using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

[ApiController]
[Route("api/laboratory/samples")]
public class SamplesController : ControllerBase
{
    private readonly ILabQueueService _labQueueService;

    public SamplesController(ILabQueueService labQueueService)
    {
        _labQueueService = labQueueService;
    }

    /// <summary>
    /// UC17 - Xem Hàng đợi Xét nghiệm (View Lab Queue)
    /// </summary>
    [HttpGet("queue")]
    public async Task<IActionResult> GetLabQueue(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? priority,
        [FromQuery] string? labRoom,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var filter = new QueueFilterDto
        {
            Status = status,
            Search = search,
            Priority = priority,
            LabRoom = labRoom,
            Page = page,
            PageSize = pageSize
        };

        var (items, totalCount) = await _labQueueService.GetQueueAsync(filter);

        return Ok(new
        {
            success = true,
            total = totalCount,
            page,
            pageSize,
            data = items
        });
    }

    /// <summary>
    /// Lấy chi tiết mẫu theo ID hoặc Barcode
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetSampleDetails(string id)
    {
        var sample = await _labQueueService.GetSampleByIdAsync(id);
        if (sample == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy mẫu xét nghiệm." });
        }

        return Ok(new { success = true, data = sample });
    }

    /// <summary>
    /// Cập nhật trạng thái mẫu (Received -> Processing -> Completed, etc.)
    /// </summary>
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateSampleStatus(string id, [FromBody] UpdateSampleStatusDto request)
    {
        var success = await _labQueueService.UpdateStatusAsync(id, request);
        if (!success)
        {
            return NotFound(new { success = false, message = "Không tìm thấy mẫu hoặc trạng thái không hợp lệ." });
        }

        return Ok(new { success = true, message = $"Cập nhật trạng thái mẫu sang '{request.Status}' thành công." });
    }

    /// <summary>
    /// UC47 - Yêu cầu chạy lại mẫu (Request Sample Rerun)
    /// </summary>
    [HttpPost("{id}/rerun")]
    public async Task<IActionResult> RequestSampleRerun(string id, [FromBody] SampleRerunRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { success = false, message = "Lý do yêu cầu chạy lại là bắt buộc." });
        }

        var success = await _labQueueService.RequestRerunAsync(id, request);
        if (!success)
        {
            return NotFound(new { success = false, message = "Không tìm thấy mẫu để yêu cầu chạy lại." });
        }

        return Ok(new { success = true, message = "Đã gửi yêu cầu chạy lại mẫu xét nghiệm thành công." });
    }
}
