
using System.Text.Json.Serialization;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;
using BMS.Application.DTOs.Users;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

[ApiController]
[Route("api/v1/laboratory/criteria")]
[Produces("application/json")]
public class TestCriteriaController : ControllerBase
{
    private readonly ITestCriterionService _criterionService;
    private readonly ITestCriterionRepository _criterionRepository;
    private readonly ILogger<TestCriteriaController> _logger;

    public TestCriteriaController(
        ITestCriterionService criterionService,
        ITestCriterionRepository criterionRepository,
        ILogger<TestCriteriaController> logger)
    {
        _criterionService = criterionService;
        _criterionRepository = criterionRepository;
        _logger = logger;
    }

    /// <summary>
    /// Lấy danh sách xét nghiệm cho chức năng quản lý gói dịch vụ.
    /// Giữ tương thích với frontend đang gọi GET /api/test-criteria.
    /// </summary>
    [HttpGet("~/api/test-criteria")]
    [ProducesResponseType(
        typeof(List<TestCriterionDto>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TestCriterionDto>>>
        GetAll(CancellationToken ct)
    {
        var items = await _criterionRepository.GetAllAsync(ct);

        var result = items.Select(x => new TestCriterionDto(
            x.Id,
            x.Code,
            x.Name,
            x.Unit,
            x.Category
        )).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách tiêu chí xét nghiệm có phân trang.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResult<TestCriterionResponseDto>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCriteria(
        [FromQuery] TestCriterionQueryParameters parameters,
        CancellationToken ct = default)
    {
        var result = await _criterionService
            .GetCriteriaPagedAsync(parameters, ct);

        return Ok(
            ApiResponse<PagedResult<TestCriterionResponseDto>>.Ok(
                result,
                "Test criteria retrieved successfully."));
    }

    /// <summary>
    /// Lấy chi tiết tiêu chí xét nghiệm.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(
        typeof(ApiResponse<TestCriterionResponseDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<object>),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCriterionById(
        string id,
        CancellationToken ct = default)
    {
        var criterion = await _criterionService
            .GetCriterionByIdAsync(id, ct);

        if (criterion == null)
        {
            return NotFound(
                ApiResponse<object>.Fail(
                    "Test criterion not found.",
                    "MSG-ERR-CRITERIA-NOT-FOUND"));
        }

        return Ok(
            ApiResponse<TestCriterionResponseDto>.Ok(
                criterion,
                "Test criterion loaded successfully."));
    }

    /// <summary>
    /// Tạo tiêu chí xét nghiệm mới.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(
        typeof(ApiResponse<TestCriterionResponseDto>),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(ApiResponse<object>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiResponse<object>),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCriterion(
        [FromBody] CreateTestCriterionDto dto,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(
                ApiResponse<object>.Fail(
                    "Invalid test criterion payload.",
                    "MSG-ERR-VALIDATION"));
        }

        _logger.LogInformation(
            "[BMS Lab API] Creating test criterion with code {CriterionCode}",
            dto.CriterionCode);

        var (success, message, data) =
            await _criterionService.CreateCriterionAsync(dto, ct);

        if (!success)
        {
            if (message.Contains(
                "already exists",
                StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(
                    ApiResponse<object>.Fail(
                        message,
                        "MSG-ERR-CRITERIA-EXISTS"));
            }

            return BadRequest(
                ApiResponse<object>.Fail(
                    message,
                    "MSG-ERR-CRITERIA-INVALID-RANGE"));
        }

        return CreatedAtAction(
            nameof(GetCriterionById),
            new { id = data!.Id },
            ApiResponse<TestCriterionResponseDto>.Ok(data, message));
    }

    /// <summary>
    /// Cập nhật tiêu chí xét nghiệm.
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(
        typeof(ApiResponse<TestCriterionResponseDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<object>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiResponse<object>),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCriterion(
        string id,
        [FromBody] UpdateTestCriterionDto dto,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(
                ApiResponse<object>.Fail(
                    "Invalid update payload.",
                    "MSG-ERR-VALIDATION"));
        }

        _logger.LogInformation(
            "[BMS Lab API] Updating test criterion {CriterionId}",
            id);

        var (success, message, data) =
            await _criterionService.UpdateCriterionAsync(id, dto, ct);

        if (!success)
        {
            if (message.Contains(
                "not found",
                StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(
                    ApiResponse<object>.Fail(
                        message,
                        "MSG-ERR-CRITERIA-NOT-FOUND"));
            }

            return BadRequest(
                ApiResponse<object>.Fail(
                    message,
                    "MSG-ERR-CRITERIA-UPDATE-FAILED"));
        }

        return Ok(
            ApiResponse<TestCriterionResponseDto>.Ok(data!, message));
    }

    /// <summary>
    /// Ngừng kích hoạt tiêu chí xét nghiệm,
    /// không xóa dữ liệu lịch sử.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(
        typeof(ApiResponse<object>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<object>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiResponse<object>),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateCriterion(
        string id,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[BMS Lab API] Deactivating test criterion {CriterionId}",
            id);

        var (success, message) =
            await _criterionService.DeactivateCriterionAsync(id, ct);

        if (!success)
        {
            if (message.Contains(
                "not found",
                StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(
                    ApiResponse<object>.Fail(
                        message,
                        "MSG-ERR-CRITERIA-NOT-FOUND"));
            }

            return BadRequest(
                ApiResponse<object>.Fail(
                    message,
                    "MSG-ERR-CRITERIA-DEACTIVATE-FAILED"));
        }

        return Ok(
            ApiResponse<object>.Ok(
                new { Id = id },
                message));
    }
}

/// <summary>
/// DTO tương thích với API danh sách xét nghiệm cho frontend.
/// </summary>
public sealed record TestCriterionDto(
    [property: JsonPropertyName("_id")] string Id,
    string Code,
    string Name,
    string Unit,
    string Category
);

