using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System.Security.Claims;
using BMS.Application.DTOs.TestBatches;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing laboratory analyzer run batches and sample groupings (UC38 / Iteration 2).
/// Follows ISO 15189 Quality Control guidelines &amp; Well Plate Layout invariants.
/// </summary>
[ApiController]
[Route("api/test-batches")]
[Route("api/v1/test-batches")]
[Produces("application/json")]
// WHY: UC38: Lab; Manager view. Web only. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class TestBatchesController(ITestBatchService testBatchService) : ControllerBase
{
    /// <summary>
    /// Retrieves test batches with optional filters (date range, analyzerId, status).
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet]
    public async Task<IActionResult> GetBatches(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? analyzerId,
        [FromQuery] string? status,
        CancellationToken ct = default)
    {
        var batches = await testBatchService.GetBatchesAsync(fromDate, toDate, analyzerId, status, ct);
        return Ok(new
        {
            success = true,
            total = batches.Count,
            data = batches
        });
    }

    /// <summary>
    /// Retrieves samples that have been received but not yet assigned to any active batch.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("pending-samples")]
    public async Task<IActionResult> GetPendingSamples(
        [FromQuery] string? category,
        CancellationToken ct = default)
    {
        var samples = await testBatchService.GetPendingSamplesAsync(category, ct);
        return Ok(new
        {
            success = true,
            total = samples.Count,
            data = samples
        });
    }

    /// <summary>
    /// Retrieves single test batch details including plate layout positions.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBatchById(string id, CancellationToken ct = default)
    {
        var batch = await testBatchService.GetByIdAsync(id, ct);
        if (batch == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Test batch with ID '{id}' was not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = batch
        });
    }

    /// <summary>
    /// Creates a new test batch with ISO 15189 plate positions (A1/A2 reserved for IQC).
    /// </summary>
    [Authorize(Roles = RoleNames.Lab)]
    [HttpPost]
    public async Task<IActionResult> CreateBatch(
        [FromBody] CreateTestBatchRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, errors = ModelState });
        }

        var technicianId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
            ?? User.FindFirst("sub")?.Value 
            ?? "TECH-SYSTEM";
        var technicianName = User.FindFirst(ClaimTypes.Name)?.Value 
            ?? User.Identity?.Name 
            ?? "Lab Technician";

        try
        {
            var created = await testBatchService.CreateBatchAsync(request, technicianId, technicianName, ct);
            return CreatedAtAction(nameof(GetBatchById), new { id = created.Id }, new
            {
                success = true,
                message = "Test batch created successfully.",
                data = created
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Updates batch status (e.g., Pending -> Running -> Completed/Cancelled).
    /// </summary>
    [Authorize(Roles = RoleNames.Lab)]
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(
        string id,
        [FromBody] UpdateTestBatchStatusRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, errors = ModelState });
        }

        try
        {
            var updated = await testBatchService.UpdateStatusAsync(id, request, ct);
            return Ok(new
            {
                success = true,
                message = $"Test batch status updated to '{request.Status}'.",
                data = updated
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Cancels a batch and rolls back assigned samples to Received status.
    /// </summary>
    [Authorize(Roles = RoleNames.Lab)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> CancelBatch(
        string id,
        [FromQuery] string? reason,
        CancellationToken ct = default)
    {
        try
        {
            var success = await testBatchService.CancelBatchAsync(id, reason, ct);
            if (!success)
            {
                return NotFound(new { success = false, message = $"Test batch with ID '{id}' was not found." });
            }

            return Ok(new
            {
                success = true,
                message = "Test batch cancelled successfully and assigned samples rolled back to Received."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
