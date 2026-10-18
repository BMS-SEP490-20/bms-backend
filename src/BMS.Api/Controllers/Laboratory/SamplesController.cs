using System.Security.Claims;
using BMS.Api.Authorization;
using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing laboratory specimens, sample collection time recording (Record Sample Time),
/// and ISO 15189 sample status lifecycle management (Update Sample Status).
/// </summary>
[ApiController]
[Route("api/laboratory/samples")]
[Route("api/v1/laboratory/samples")]
[Route("api/samples")]
[Route("api/v1/samples")]
[Produces("application/json")]
public class SamplesController(ILabQueueService labQueueService) : ControllerBase
{
    /// <summary>
    /// UC17 - Xem Hàng đợi Xét nghiệm (View Lab Queue)
    /// </summary>
    [HttpGet]
    [HttpGet("queue")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLabQueue(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? priority,
        [FromQuery] string? labRoom,
        [FromQuery] string? patientId,
        [FromQuery] string? appointmentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var filter = new QueueFilterDto
        {
            Status = status,
            Search = search,
            Priority = priority,
            LabRoom = labRoom,
            PatientId = patientId,
            AppointmentId = appointmentId,
            Page = page,
            PageSize = pageSize
        };

        var (items, totalCount) = await labQueueService.GetQueueAsync(filter);

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
    /// Lấy chi tiết mẫu theo ID, Barcode hoặc RFID tag.
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSampleDetails(string id)
    {
        var sample = await labQueueService.GetSampleByIdAsync(id);
        if (sample == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = $"Không tìm thấy mẫu xét nghiệm '{id}'.",
                Extensions = { ["code"] = "SAMPLE_NOT_FOUND", ["traceId"] = HttpContext.TraceIdentifier }
            });
        }

        return Ok(new { success = true, data = sample });
    }

    /// <summary>
    /// Ghi nhận thời gian lấy mẫu bệnh phẩm cho một mẫu đã có theo ID hoặc Barcode (Record Sample Time use case).
    /// </summary>
    [HttpPost("{id}/collection-time")]
    [HttpPatch("{id}/collection-time")]
    [HttpPut("{id}/collection-time")]
    [HasPermission($"{Permissions.CollectSample},{Permissions.ReceiveSample},{Permissions.ViewAllLab}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordSampleTimeById(
        string id,
        [FromBody] RecordSampleCollectionRequest request,
        CancellationToken ct)
    {
        var updated = await labQueueService.RecordSampleCollectionTimeAsync(id, request, CurrentUserId, ct);
        return Ok(new
        {
            success = true,
            message = $"Đã ghi nhận thời gian lấy mẫu '{updated.Barcode}' lúc {updated.CollectedAt:yyyy-MM-dd HH:mm:ss} UTC.",
            data = updated
        });
    }

    /// <summary>
    /// Tạo mới hoặc ghi nhận thời gian lấy mẫu bệnh phẩm gắn với Bệnh nhân và Lịch hẹn (Record Sample Time use case).
    /// </summary>
    [HttpPost("record-collection")]
    [HttpPost]
    [HasPermission($"{Permissions.CollectSample},{Permissions.ReceiveSample},{Permissions.ViewAllLab}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordSampleCollection(
        [FromBody] RecordSampleCollectionRequest request,
        CancellationToken ct)
    {
        var recorded = await labQueueService.RecordSampleCollectionTimeAsync(null, request, CurrentUserId, ct);
        return CreatedAtAction(nameof(GetSampleDetails), new { id = recorded.Id }, new
        {
            success = true,
            message = $"Đã ghi nhận mẫu xét nghiệm '{recorded.Id}' (Barcode: {recorded.Barcode}) lúc {recorded.CollectedAt:yyyy-MM-dd HH:mm:ss} UTC.",
            data = recorded
        });
    }

    /// <summary>
    /// Cập nhật trạng thái vòng đời mẫu bệnh phẩm với kiểm tra chuyển trạng thái ISO 15189 hợp lệ (Update Sample Status use case).
    /// </summary>
    [HttpPut("{id}/status")]
    [HttpPatch("{id}/status")]
    [HasPermission($"{Permissions.ReceiveSample},{Permissions.RunAnalyzer},{Permissions.EnterResult},{Permissions.CollectSample},{Permissions.ViewAllLab}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSampleStatus(
        string id,
        [FromBody] UpdateSampleStatusDto request,
        CancellationToken ct)
    {
        var updated = await labQueueService.UpdateSampleStatusDetailedAsync(id, request, CurrentUserId, ct);
        return Ok(new
        {
            success = true,
            message = $"Cập nhật trạng thái mẫu '{updated.Barcode}' sang '{updated.Status}' thành công.",
            data = updated
        });
    }

    /// <summary>
    /// UC47 - Yêu cầu chạy lại mẫu (Request Sample Rerun)
    /// </summary>
    [HttpPost("{id}/rerun")]
    [HasPermission($"{Permissions.RunAnalyzer},{Permissions.EnterResult},{Permissions.ApproveResult},{Permissions.ViewAllLab}")]
    public async Task<IActionResult> RequestSampleRerun(string id, [FromBody] SampleRerunRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { success = false, message = "Lý do yêu cầu chạy lại là bắt buộc." });
        }

        var success = await labQueueService.RequestRerunAsync(id, request);
        if (!success)
        {
            return NotFound(new { success = false, message = "Không tìm thấy mẫu để yêu cầu chạy lại." });
        }

        return Ok(new { success = true, message = "Đã gửi yêu cầu chạy lại mẫu xét nghiệm thành công." });
    }

    private string? CurrentUserId =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
}
