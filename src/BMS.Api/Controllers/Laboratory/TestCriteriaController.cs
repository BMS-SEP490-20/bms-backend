using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;
using BMS.Application.DTOs.Users;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller providing full CRUD management for clinical laboratory test criteria (UC18).
/// Enforces ISO 15189 reference ranges, Panic alert boundaries, and safe soft-deactivation.
/// </summary>
[ApiController]
[Route("api/v1/laboratory/criteria")]
[Produces("application/json")]
public class TestCriteriaController : ControllerBase
{
    private readonly ITestCriterionService _criterionService;
    private readonly ILogger<TestCriteriaController> _logger;

    public TestCriteriaController(
        ITestCriterionService criterionService,
        ILogger<TestCriteriaController> logger)
    {
        _criterionService = criterionService;
        _logger = logger;
    }

    /// <summary>
    /// Tra cứu danh mục chỉ số xét nghiệm có phân trang server-side và bộ lọc chuyên khoa (UC18).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TestCriterionResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCriteria([FromQuery] TestCriterionQueryParameters parameters, CancellationToken ct = default)
    {
        var result = await _criterionService.GetCriteriaPagedAsync(parameters, ct);
        return Ok(ApiResponse<PagedResult<TestCriterionResponseDto>>.Ok(result, "Test criteria retrieved successfully."));
    }

    /// <summary>
    /// Tra cứu chi tiết một chỉ số xét nghiệm theo mã định danh duy nhất (UC18).
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<TestCriterionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCriterionById(string id, CancellationToken ct = default)
    {
        var criterion = await _criterionService.GetCriterionByIdAsync(id, ct);
        if (criterion == null)
        {
            return NotFound(ApiResponse<object>.Fail("Test criterion not found.", "MSG-ERR-CRITERIA-NOT-FOUND"));
        }

        return Ok(ApiResponse<TestCriterionResponseDto>.Ok(criterion, "Test criterion loaded successfully."));
    }

    /// <summary>
    /// Tạo mới một chỉ số xét nghiệm y tế với dải tham chiếu và ngưỡng Panic (UC18).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<TestCriterionResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCriterion([FromBody] CreateTestCriterionDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<object>.Fail("Invalid test criterion payload.", "MSG-ERR-VALIDATION"));
        }

        _logger.LogInformation("[BMS Lab API] Creating test criterion with code {CriterionCode}", dto.CriterionCode);
        var (success, message, data) = await _criterionService.CreateCriterionAsync(dto, ct);

        if (!success)
        {
            if (message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(ApiResponse<object>.Fail(message, "MSG-ERR-CRITERIA-EXISTS"));
            }
            return BadRequest(ApiResponse<object>.Fail(message, "MSG-ERR-CRITERIA-INVALID-RANGE"));
        }

        return CreatedAtAction(nameof(GetCriterionById), new { id = data!.Id }, ApiResponse<TestCriterionResponseDto>.Ok(data, message));
    }

    /// <summary>
    /// Cập nhật dải tham chiếu, đơn vị đo, và thông tin chuyên môn của chỉ số xét nghiệm (UC18).
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<TestCriterionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCriterion(string id, [FromBody] UpdateTestCriterionDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<object>.Fail("Invalid update payload.", "MSG-ERR-VALIDATION"));
        }

        _logger.LogInformation("[BMS Lab API] Updating test criterion {CriterionId}", id);
        var (success, message, data) = await _criterionService.UpdateCriterionAsync(id, dto, ct);

        if (!success)
        {
            if (message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(ApiResponse<object>.Fail(message, "MSG-ERR-CRITERIA-NOT-FOUND"));
            }
            return BadRequest(ApiResponse<object>.Fail(message, "MSG-ERR-CRITERIA-UPDATE-FAILED"));
        }

        return Ok(ApiResponse<TestCriterionResponseDto>.Ok(data!, message));
    }

    /// <summary>
    /// Khóa / Vô hiệu hóa an toàn chỉ số xét nghiệm theo tiêu chuẩn ISO 15189 (UC18).
    /// Tuyệt đối không xóa cứng để bảo vệ tính toàn vẹn hồ sơ bệnh án cũ.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateCriterion(string id, CancellationToken ct = default)
    {
        _logger.LogInformation("[BMS Lab API] Deactivating test criterion {CriterionId}", id);
        var (success, message) = await _criterionService.DeactivateCriterionAsync(id, ct);

        if (!success)
        {
            if (message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(ApiResponse<object>.Fail(message, "MSG-ERR-CRITERIA-NOT-FOUND"));
            }
            return BadRequest(ApiResponse<object>.Fail(message, "MSG-ERR-CRITERIA-DEACTIVATE-FAILED"));
        }

        return Ok(ApiResponse<object>.Ok(new { Id = id }, message));
    }
}
