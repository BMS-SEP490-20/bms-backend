using BMS.Core.Constants;
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
// WHY: UC17/40/45/47: queue Lab, Manager (view); collection Lab; status Lab, Manager; rerun Lab; rerun view + Doctor. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class SamplesController(ILabQueueService labQueueService, IQcService? qcService = null) : ControllerBase
{
    /// <summary>
    /// UC17 - Xem Hàng đợi Xét nghiệm (View Lab Queue)
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet]
    [HttpGet("queue")]
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
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("{id}")]
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
    [Authorize(Roles = RoleNames.Lab)]
    [HttpPost("{id}/collection-time")]
    [HttpPatch("{id}/collection-time")]
    [HttpPut("{id}/collection-time")]
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
    [Authorize(Roles = RoleNames.Lab)]
    [HttpPost("record-collection")]
    [HttpPost]
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
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpPut("{id}/status")]
    [HttpPatch("{id}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> UpdateSampleStatus(
        string id,
        [FromBody] UpdateSampleStatusDto request,
        CancellationToken ct)
    {
        // RÀNG BUỘC NGHIỆP VỤ Y TẾ (ISO 15189 / Westgard IQC Multi-Rules):
        // Nếu chuyển trạng thái sang Testing (Đang phân tích), kiểm tra trạng thái máy xét nghiệm / cờ vi phạm IQC
        if (!string.IsNullOrWhiteSpace(request.Status) &&
            request.Status.Trim().Equals("Testing", StringComparison.OrdinalIgnoreCase) &&
            qcService != null)
        {
            var activeFlags = await qcService.GetActiveQcFlagsAsync(ct);
            var lockedOrRejected = activeFlags.FirstOrDefault(f =>
                f.IsLocked ||
                (f.ViolatedRules != null && f.ViolatedRules.Any(r => r.Contains("1_3s", StringComparison.OrdinalIgnoreCase))) ||
                (f.Status != null && f.Status.Contains("REJECT_1_3S", StringComparison.OrdinalIgnoreCase)));

            if (lockedOrRejected != null)
            {
                var ruleText = lockedOrRejected.ViolatedRules != null && lockedOrRejected.ViolatedRules.Count > 0
                    ? string.Join(", ", lockedOrRejected.ViolatedRules)
                    : "Westgard 1_3s";

                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Máy xét nghiệm đang bị khóa do vi phạm nội kiểm IQC (Westgard 1_3s).",
                    Detail = $"Không thể đưa mẫu xét nghiệm '{id}' vào phân tích (Testing). Thiết bị đo đang bị KHÓA BẢO VỆ do vi phạm quy tắc nội kiểm {ruleText}. Bắt buộc Kỹ thuật viên trưởng xử lý sự cố, ký duyệt khắc phục (Corrective Action) trước khi phân tích mẫu bệnh nhân.",
                    Extensions =
                    {
                        ["code"] = "ANALYZER_QC_LOCKED",
                        ["runId"] = lockedOrRejected.RunId,
                        ["status"] = lockedOrRejected.Status,
                        ["violatedRules"] = lockedOrRejected.ViolatedRules,
                        ["traceId"] = HttpContext.TraceIdentifier
                    }
                });
            }
        }

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
    [Authorize(Roles = RoleNames.Lab)]
    [HttpPost("{id}/rerun")]
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

    /// <summary>
    /// UC47 - Lấy danh sách yêu cầu chạy lại mẫu đang mở (Open Sample Rerun Requests)
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet("reruns")]
    public async Task<IActionResult> GetRerunRequests()
    {
        var reruns = await labQueueService.GetRerunRequestsAsync();
        return Ok(new { success = true, data = reruns });
    }

    /// <summary>
    /// UC47 - Lấy thông tin chi tiết để yêu cầu chạy lại mẫu (Sample Rerun Details & Context)
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet("{id}/rerun-details")]
    public async Task<IActionResult> GetSampleRerunDetails(string id)
    {
        var details = await labQueueService.GetSampleRerunDetailsAsync(id);
        if (details == null)
        {
            return NotFound(new { success = false, message = $"Sample '{id}' not found for rerun requisition." });
        }

        return Ok(new { success = true, data = details });
    }

    private string? CurrentUserId =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
}
