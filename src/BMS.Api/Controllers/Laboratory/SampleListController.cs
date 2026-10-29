using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing laboratory sample collection intake lists and tube barcode generation (UC37 / Iteration 2).
/// Conforms to: Production Standards, Clean Architecture & AGENTS.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// WHY: UC37: Lab (absorbs Phlebotomist). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleNames.Lab)]
public class SampleListController : ControllerBase
{
    private readonly ISampleListService _sampleListService;

    public SampleListController(ISampleListService sampleListService)
    {
        _sampleListService = sampleListService;
    }

    /// <summary>
    /// Retrieves checked-in appointments available for phlebotomy batch intake.
    /// </summary>
    [HttpGet("appointments")]
    public async Task<IActionResult> GetCheckedInAppointments()
    {
        var appointments = await _sampleListService.GetCheckedInAppointmentsAsync();
        return Ok(new
        {
            success = true,
            data = appointments
        });
    }

    /// <summary>
    /// Retrieves sample collection list batches for today or specified date.
    /// </summary>
    [HttpGet("batches")]
    public async Task<IActionResult> GetBatches([FromQuery] DateTime? date)
    {
        var batches = await _sampleListService.GetCollectionBatchesAsync(date);
        return Ok(new
        {
            success = true,
            data = batches
        });
    }

    /// <summary>
    /// Retrieves specific sample collection batch by batch code or ID.
    /// </summary>
    [HttpGet("batches/{batchCode}")]
    public async Task<IActionResult> GetBatchByCode(string batchCode)
    {
        var batch = await _sampleListService.GetBatchByCodeAsync(batchCode);
        if (batch == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Collection batch '{batchCode}' not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = batch
        });
    }

    /// <summary>
    /// Confirms and creates a new sample collection list batch for phlebotomy (UC37).
    /// </summary>
    [HttpPost("batches")]
    [ProducesResponseType(typeof(SampleListResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateBatch([FromBody] CreateSampleListRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Validation failed.",
                errors = ModelState
            });
        }

        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(currentUserId) && string.IsNullOrEmpty(request.CollectorId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "Vui lòng đăng nhập với tài khoản nhân viên lấy mẫu."
            });
        }

        try
        {
            var created = await _sampleListService.CreateCollectionListAsync(request, currentUserId);

            return CreatedAtAction(nameof(GetBatchByCode), new { batchCode = created.BatchCode }, new
            {
                success = true,
                message = $"Sample collection batch '{created.BatchCode}' confirmed successfully.",
                data = created
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Generates printable Code 128 barcodes for all specimen tubes in a batch.
    /// </summary>
    [HttpGet("batches/{batchCode}/barcodes")]
    public async Task<IActionResult> GetBatchBarcodes(string batchCode)
    {
        var barcodes = await _sampleListService.GenerateBatchBarcodesAsync(batchCode);
        if (barcodes == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Collection batch '{batchCode}' not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = barcodes
        });
    }
}
