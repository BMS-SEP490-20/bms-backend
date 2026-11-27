using BMS.Core.Constants;
using BMS.Core.Exceptions;
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
        try
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
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                code = ex.Code,
                title = ex.Message,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = StatusCodes.Status500InternalServerError,
                code = "INTERNAL_SERVER_ERROR",
                title = "Không thể tải danh sách hàng đợi mẫu xét nghiệm lúc này.",
                detail = "Không thể tải danh sách hàng đợi mẫu xét nghiệm lúc này.",
                message = "Không thể tải danh sách hàng đợi mẫu xét nghiệm lúc này."
            });
        }
    }

    /// <summary>
    /// Lấy danh sách nhân sự lâm sàng và xét nghiệm đang hoạt động từ MongoDB phục vụ chọn Người Lấy Mẫu, Người Bàn Giao và KTV Tiếp Nhận (UC40/UC41).
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaffOrReception)]
    [HttpGet("staff")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLaboratoryStaff(CancellationToken ct)
    {
        try
        {
            var staff = await labQueueService.GetStaffDirectoryAsync(ct);
            return Ok(new
            {
                success = true,
                total = staff.Count,
                data = staff
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = StatusCodes.Status500InternalServerError,
                code = "INTERNAL_SERVER_ERROR",
                title = "Không thể tải danh sách nhân sự phòng xét nghiệm từ cơ sở dữ liệu.",
                detail = "Không thể tải danh sách nhân sự phòng xét nghiệm từ cơ sở dữ liệu.",
                message = "Không thể tải danh sách nhân sự phòng xét nghiệm từ cơ sở dữ liệu."
            });
        }
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
        try
        {
            var sample = await labQueueService.GetSampleByIdAsync(id);
            if (sample == null)
            {
                return NotFound(new
                {
                    success = false,
                    status = StatusCodes.Status404NotFound,
                    code = "SAMPLE_NOT_FOUND",
                    title = $"Không tìm thấy mẫu xét nghiệm '{id}'.",
                    detail = $"Không tìm thấy mẫu xét nghiệm '{id}'.",
                    message = $"Không tìm thấy mẫu xét nghiệm '{id}'."
                });
            }

            return Ok(new { success = true, data = sample });
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                code = ex.Code,
                title = ex.Message,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = StatusCodes.Status500InternalServerError,
                code = "INTERNAL_SERVER_ERROR",
                title = "Không thể tải thông tin chi tiết mẫu xét nghiệm.",
                detail = "Không thể tải thông tin chi tiết mẫu xét nghiệm.",
                message = "Không thể tải thông tin chi tiết mẫu xét nghiệm."
            });
        }
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
        try
        {
            var updated = await labQueueService.RecordSampleCollectionTimeAsync(id, request, CurrentUserId, ct);
            return Ok(new
            {
                success = true,
                message = $"Đã ghi nhận thời gian lấy mẫu '{updated.Barcode}' lúc {updated.CollectedAt:yyyy-MM-dd HH:mm:ss} UTC.",
                data = updated
            });
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                code = ex.Code,
                title = ex.Message,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = StatusCodes.Status500InternalServerError,
                code = "INTERNAL_SERVER_ERROR",
                title = "Không thể ghi nhận thời gian lấy mẫu lúc này. Vui lòng thử lại sau.",
                detail = "Không thể ghi nhận thời gian lấy mẫu lúc này. Vui lòng thử lại sau.",
                message = "Không thể ghi nhận thời gian lấy mẫu lúc này. Vui lòng thử lại sau."
            });
        }
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
        try
        {
            var recorded = await labQueueService.RecordSampleCollectionTimeAsync(null, request, CurrentUserId, ct);
            return StatusCode(StatusCodes.Status201Created, new
            {
                success = true,
                message = $"Đã ghi nhận mẫu xét nghiệm '{recorded.Id}' (Barcode: {recorded.Barcode}) lúc {recorded.CollectedAt:yyyy-MM-dd HH:mm:ss} UTC.",
                data = recorded
            });
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                code = ex.Code,
                title = ex.Message,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = StatusCodes.Status500InternalServerError,
                code = "INTERNAL_SERVER_ERROR",
                title = "Không thể tạo mới và ghi nhận thời gian lấy mẫu lúc này.",
                detail = "Không thể tạo mới và ghi nhận thời gian lấy mẫu lúc này.",
                message = "Không thể tạo mới và ghi nhận thời gian lấy mẫu lúc này."
            });
        }
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
            // Chỉ chặn khi máy còn IsLocked. Sau Manager ký corrective action, IsLocked=false
            // nhưng Status vẫn giữ REJECT_1_3S (lịch sử IQC) — không được tiếp tục khóa Testing.
            var lockedFlag = activeFlags.FirstOrDefault(f => f.IsLocked);

            if (lockedFlag != null)
            {
                var ruleText = lockedFlag.ViolatedRules != null && lockedFlag.ViolatedRules.Count > 0
                    ? string.Join(", ", lockedFlag.ViolatedRules)
                    : "Westgard 1_3s";

                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Máy xét nghiệm đang bị khóa do vi phạm nội kiểm IQC (Westgard 1_3s).",
                    Detail = $"Không thể đưa mẫu xét nghiệm '{id}' vào phân tích (Testing). Thiết bị đo đang bị KHÓA BẢO VỆ do vi phạm quy tắc nội kiểm {ruleText}. Bắt buộc Kỹ thuật viên trưởng xử lý sự cố, ký duyệt khắc phục (Corrective Action) trước khi phân tích mẫu bệnh nhân.",
                    Extensions =
                    {
                        ["code"] = "ANALYZER_QC_LOCKED",
                        ["runId"] = lockedFlag.RunId,
                        ["status"] = lockedFlag.Status,
                        ["violatedRules"] = lockedFlag.ViolatedRules,
                        ["traceId"] = HttpContext.TraceIdentifier
                    }
                });
            }
        }

        try
        {
            var updated = await labQueueService.UpdateSampleStatusDetailedAsync(id, request, CurrentUserId, ct);
            return Ok(new
            {
                success = true,
                message = $"Cập nhật trạng thái mẫu '{updated.Barcode}' sang '{updated.Status}' thành công.",
                data = updated
            });
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                code = ex.Code,
                title = ex.Message,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = StatusCodes.Status500InternalServerError,
                code = "INTERNAL_SERVER_ERROR",
                title = "Không thể cập nhật trạng thái mẫu xét nghiệm.",
                detail = "Không thể cập nhật trạng thái mẫu xét nghiệm.",
                message = "Không thể cập nhật trạng thái mẫu xét nghiệm."
            });
        }
    }

    /// <summary>
    /// UC47 - Yêu cầu chạy lại mẫu (Request Sample Rerun)
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpPost("{id}/rerun")]
    public async Task<IActionResult> RequestSampleRerun(string id, [FromBody] SampleRerunRequestDto request)
    {
        try
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
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                code = ex.Code,
                title = ex.Message,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = StatusCodes.Status500InternalServerError,
                code = "INTERNAL_SERVER_ERROR",
                title = "Không thể gửi yêu cầu chạy lại mẫu xét nghiệm.",
                detail = "Không thể gửi yêu cầu chạy lại mẫu xét nghiệm.",
                message = "Không thể gửi yêu cầu chạy lại mẫu xét nghiệm."
            });
        }
    }

    /// <summary>
    /// UC47 - Lấy danh sách yêu cầu chạy lại mẫu đang mở (Open Sample Rerun Requests)
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet("reruns")]
    public async Task<IActionResult> GetRerunRequests()
    {
        try
        {
            var reruns = await labQueueService.GetRerunRequestsAsync();
            return Ok(new { success = true, data = reruns });
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                code = ex.Code,
                title = ex.Message,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = StatusCodes.Status500InternalServerError,
                code = "INTERNAL_SERVER_ERROR",
                title = "Không thể tải danh sách yêu cầu chạy lại mẫu.",
                detail = "Không thể tải danh sách yêu cầu chạy lại mẫu.",
                message = "Không thể tải danh sách yêu cầu chạy lại mẫu."
            });
        }
    }

    /// <summary>
    /// UC47 - Lấy thông tin chi tiết để yêu cầu chạy lại mẫu (Sample Rerun Details & Context)
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet("{id}/rerun-details")]
    public async Task<IActionResult> GetSampleRerunDetails(string id)
    {
        try
        {
            var details = await labQueueService.GetSampleRerunDetailsAsync(id);
            if (details == null)
            {
                return NotFound(new { success = false, message = $"Không tìm thấy mẫu '{id}' để lấy chi tiết chạy lại." });
            }

            return Ok(new { success = true, data = details });
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                code = ex.Code,
                title = ex.Message,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = StatusCodes.Status500InternalServerError,
                code = "INTERNAL_SERVER_ERROR",
                title = "Không thể tải chi tiết yêu cầu chạy lại mẫu.",
                detail = "Không thể tải chi tiết yêu cầu chạy lại mẫu.",
                message = "Không thể tải chi tiết yêu cầu chạy lại mẫu."
            });
        }
    }

    /// <summary>
    /// BR-PHL-02 - Chiết mẫu phụ (Aliquoting) từ một ống mẫu gốc đã thu thập/tiếp nhận.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpPost("{id}/aliquot")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAliquot(
        string id,
        [FromBody] CreateAliquotRequestDto request,
        CancellationToken ct)
    {
        try
        {
            var created = await labQueueService.CreateAliquotAsync(id, request ?? new CreateAliquotRequestDto(), CurrentUserId, ct);
            return StatusCode(StatusCodes.Status201Created, new
            {
                success = true,
                message = $"Đã chiết mẫu phụ (Aliquot) '{created.Barcode}' từ mẫu gốc '{created.ParentBarcode}' thành công.",
                data = created
            });
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                success = false,
                status = (int)ex.StatusCode,
                code = ex.Code,
                title = ex.Message,
                detail = ex.Message,
                message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                status = StatusCodes.Status500InternalServerError,
                code = "INTERNAL_SERVER_ERROR",
                title = "Không thể chiết mẫu phụ (Aliquot) lúc này.",
                detail = "Không thể chiết mẫu phụ (Aliquot) lúc này.",
                message = "Không thể chiết mẫu phụ (Aliquot) lúc này."
            });
        }
    }

    private string? CurrentUserId =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
}
