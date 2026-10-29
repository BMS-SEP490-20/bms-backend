using BMS.Core.Constants;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;
using BMS.Application.DTOs.Users;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing biological reference intervals and clinical evaluation engine (UC57).
/// Fully adheres to: BR-LAB-02 (Biological Reference Intervals) and BR-LAB-03 (Panic Critical Alerts).
/// </summary>
[ApiController]
[Route("api/v1/laboratory/reference-intervals")]
[Route("api/laboratory/reference-intervals")]
[Route("api/reference-intervals")]
[Produces("application/json")]
// WHY: UC57: read/evaluate Manager, Lab, Doctor; configure Manager only. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class ReferenceIntervalsController : ControllerBase
{
    private readonly IReferenceIntervalService _intervalService;
    private readonly ILogger<ReferenceIntervalsController> _logger;

    public ReferenceIntervalsController(
        IReferenceIntervalService intervalService,
        ILogger<ReferenceIntervalsController> logger)
    {
        _intervalService = intervalService;
        _logger = logger;
    }

    /// <summary>
    /// Lấy danh sách khoảng tham chiếu có phân trang và bộ lọc (UC57).
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ReferenceIntervalResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIntervals(
        [FromQuery] ReferenceIntervalQueryParameters parameters,
        CancellationToken ct = default)
    {
        var result = await _intervalService.GetIntervalsPagedAsync(parameters, ct);
        return Ok(ApiResponse<PagedResult<ReferenceIntervalResponseDto>>.Ok(
            result,
            "Danh sách khoảng tham chiếu sinh học đã tải thành công."));
    }

    /// <summary>
    /// Lấy tất cả khoảng tham chiếu đã cấu hình cho một chỉ số xét nghiệm cụ thể (theo mã chỉ số).
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet("criterion/{criterionCode}")]
    [ProducesResponseType(typeof(ApiResponse<List<ReferenceIntervalResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByCriterionCode(
        string criterionCode,
        [FromQuery] bool activeOnly = true,
        CancellationToken ct = default)
    {
        var result = await _intervalService.GetIntervalsByCriterionCodeAsync(criterionCode, activeOnly, ct);
        return Ok(ApiResponse<List<ReferenceIntervalResponseDto>>.Ok(
            result,
            $"Tải danh sách khoảng tham chiếu cho chỉ số {criterionCode.ToUpperInvariant()} thành công."));
    }

    /// <summary>
    /// Lấy thông tin chi tiết một khoảng tham chiếu theo ID.
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ReferenceIntervalResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct = default)
    {
        var interval = await _intervalService.GetIntervalByIdAsync(id, ct);
        if (interval == null)
        {
            return NotFound(ApiResponse<object>.Fail(
                "Không tìm thấy khoảng tham chiếu sinh học yêu cầu.",
                "MSG-ERR-INTERVAL-NOT-FOUND"));
        }

        return Ok(ApiResponse<ReferenceIntervalResponseDto>.Ok(
            interval,
            "Chi tiết khoảng tham chiếu đã tải thành công."));
    }

    /// <summary>
    /// Cấu hình / Thêm mới một khoảng tham chiếu sinh học (UC57).
    /// </summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ReferenceIntervalResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateInterval(
        [FromBody] CreateReferenceIntervalDto dto,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<object>.Fail("Dữ liệu cấu hình không hợp lệ.", "MSG-ERR-VALIDATION"));
        }

        _logger.LogInformation("[ReferenceIntervals API] Creating reference interval for criterion {Code}, gender {Gender}",
            dto.CriterionCode, dto.Gender);

        var (success, message, data) = await _intervalService.CreateIntervalAsync(dto, ct);
        if (!success)
        {
            return BadRequest(ApiResponse<object>.Fail(message, "MSG-ERR-INTERVAL-INVALID-RANGE"));
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = data!.Id },
            ApiResponse<ReferenceIntervalResponseDto>.Ok(data, message));
    }

    /// <summary>
    /// Cập nhật thông tin khoảng tham chiếu sinh học (UC57).
    /// </summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ReferenceIntervalResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateInterval(
        string id,
        [FromBody] UpdateReferenceIntervalDto dto,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<object>.Fail("Dữ liệu cập nhật không hợp lệ.", "MSG-ERR-VALIDATION"));
        }

        _logger.LogInformation("[ReferenceIntervals API] Updating reference interval {Id}", id);

        var (success, message, data) = await _intervalService.UpdateIntervalAsync(id, dto, ct);
        if (!success)
        {
            if (message.Contains("Không tìm thấy", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(ApiResponse<object>.Fail(message, "MSG-ERR-INTERVAL-NOT-FOUND"));
            }
            return BadRequest(ApiResponse<object>.Fail(message, "MSG-ERR-INTERVAL-UPDATE-FAILED"));
        }

        return Ok(ApiResponse<ReferenceIntervalResponseDto>.Ok(data!, message));
    }

    /// <summary>
    /// Vô hiệu hóa (khóa mềm) khoảng tham chiếu sinh học, bảo toàn dữ liệu lịch sử xét nghiệm.
    /// </summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateInterval(string id, CancellationToken ct = default)
    {
        _logger.LogInformation("[ReferenceIntervals API] Deactivating reference interval {Id}", id);

        var (success, message) = await _intervalService.DeactivateIntervalAsync(id, ct);
        if (!success)
        {
            return NotFound(ApiResponse<object>.Fail(message, "MSG-ERR-INTERVAL-NOT-FOUND"));
        }

        return Ok(ApiResponse<object>.Ok(new { Id = id }, message));
    }

    /// <summary>
    /// Đánh giá kết quả xét nghiệm theo khoảng tham chiếu chuẩn (BR-LAB-02 / BR-LAB-03).
    /// Tự động gắn cờ L (Low - Vàng cam), H (High - Đỏ) hoặc PANIC (Báo động đỏ nguy kịch).
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpPost("evaluate")]
    [ProducesResponseType(typeof(ApiResponse<EvaluateTestResultResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EvaluateResult(
        [FromBody] EvaluateTestResultRequestDto request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<object>.Fail("Dữ liệu đánh giá không hợp lệ.", "MSG-ERR-VALIDATION"));
        }

        var result = await _intervalService.EvaluateResultAsync(request, ct);
        return Ok(ApiResponse<EvaluateTestResultResponseDto>.Ok(
            result,
            result.IsPanic 
                ? "Chỉ số rơi vào khoảng PANIC nguy kịch đe dọa sinh mạng!" 
                : "Đánh giá chỉ số thành công."));
    }
}
