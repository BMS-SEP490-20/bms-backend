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
    /// Retrieves paginated list of clinical laboratory test criteria with server-side pagination and category filters (UC18).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TestCriterionResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCriteria([FromQuery] TestCriterionQueryParameters parameters, CancellationToken ct = default)
    {
        var result = await _criterionService.GetCriteriaPagedAsync(parameters, ct);
        return Ok(ApiResponse<PagedResult<TestCriterionResponseDto>>.Ok(result, "Test criteria retrieved successfully."));
    }

    /// <summary>
    /// Retrieves full clinical test criterion details by its unique identifier (UC18).
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
    /// Creates a new clinical test parameter / criterion with reference ranges and panic thresholds (UC18).
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
    /// Updates reference ranges, units, and clinical metadata for an existing test criterion (UC18).
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
    /// Safely deactivates a test criterion conforming to ISO 15189:2022 (UC18).
    /// Strictly prevents hard deletion to preserve historical patient medical records.
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
