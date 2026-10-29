using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.QualityControl;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller for Internal Quality Control (IQC) Lot Management, Westgard Multi-Rule Evaluation &amp; Levey-Jennings Charts (UC73 / Issue #I3-076).
/// Conforms to: ISO 15189 Quality Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/v1/qc")]
[Route("api/qc")]
// WHY: UC73: lots/runs Lab, Manager; unlock (CAPA sign-off) Manager only (decision 1). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class QualityControlController : ControllerBase
{
    private readonly IQcService _qcService;

    public QualityControlController(IQcService qcService)
    {
        _qcService = qcService;
    }

    /// <summary>
    /// Retrieves list of QC material lots.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("lots")]
    public async Task<IActionResult> GetLots([FromQuery] bool? activeOnly, [FromQuery] string? analyte, CancellationToken ct)
    {
        var lots = await _qcService.GetLotsAsync(activeOnly, analyte, ct);
        return Ok(new
        {
            success = true,
            data = lots,
            total = lots.Count
        });
    }

    /// <summary>
    /// Retrieves a QC lot by ID.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("lots/{id}")]
    public async Task<IActionResult> GetLotById(string id, CancellationToken ct)
    {
        var lot = await _qcService.GetLotByIdAsync(id, ct);
        if (lot == null)
        {
            return NotFound(new { success = false, message = $"Không tìm thấy lô QC với mã '{id}'." });
        }
        return Ok(new { success = true, data = lot });
    }

    /// <summary>
    /// Creates a new QC lot with specified target mean and standard deviation.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpPost("lots")]
    public async Task<IActionResult> CreateLot([FromBody] CreateQcLotDto dto, CancellationToken ct)
    {
        try
        {
            var created = await _qcService.CreateLotAsync(dto, ct);
            return CreatedAtAction(nameof(GetLotById), new { id = created.Id }, new
            {
                success = true,
                data = created,
                message = "Đã khởi tạo lô QC mới thành công."
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Updates an existing QC lot.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpPut("lots/{id}")]
    public async Task<IActionResult> UpdateLot(string id, [FromBody] UpdateQcLotDto dto, CancellationToken ct)
    {
        try
        {
            var updated = await _qcService.UpdateLotAsync(id, dto, ct);
            return Ok(new
            {
                success = true,
                data = updated,
                message = "Đã cập nhật thông tin lô QC thành công."
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Toggles active status of a QC lot.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpPost("lots/{id}/toggle-status")]
    public async Task<IActionResult> ToggleLotStatus(string id, CancellationToken ct)
    {
        try
        {
            var active = await _qcService.ToggleLotStatusAsync(id, ct);
            return Ok(new
            {
                success = true,
                isActive = active,
                message = active ? "Đã kích hoạt lô QC." : "Đã tạm dừng lô QC."
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Records an analytical IQC measurement run and executes Westgard multi-rule evaluation.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpPost("runs/record")]
    public async Task<IActionResult> RecordRun([FromBody] RecordQcRunDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _qcService.RecordRunAsync(dto, ct);
            return Ok(new
            {
                success = true,
                data = result,
                message = result.Message
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Retrieves Levey-Jennings chart data points and boundary markers (+/-1s, +/-2s, +/-3s) for visual review.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("levey-jennings/{lotId}")]
    public async Task<IActionResult> GetLeveyJenningsData(string lotId, CancellationToken ct)
    {
        try
        {
            var chartData = await _qcService.GetLeveyJenningsDataAsync(lotId, ct);
            return Ok(new
            {
                success = true,
                data = chartData
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Signs off on corrective action for an out-of-control QC run, unlocking the analyzer batch.
    /// </summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpPost("runs/{runId}/signoff")]
    public async Task<IActionResult> SignoffCorrectiveAction(string runId, [FromBody] QcSignoffRequestDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _qcService.SignoffCorrectiveActionAsync(runId, dto, ct);
            return Ok(new
            {
                success = true,
                data = result,
                message = result.Message
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Retrieves executive QC surveillance summary metrics.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var summary = await _qcService.GetDashboardSummaryAsync(ct);
        return Ok(new
        {
            success = true,
            data = summary
        });
    }
}
