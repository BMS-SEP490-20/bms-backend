using System.Security.Claims;
using System.Threading.Tasks;
using BMS.Application.DTOs.IoT;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.IoT;

/// <summary>
/// Controller for configuring IoT environmental sensor thresholds, alert boundaries, and recipient escalation (UC67 / Iteration 3).
/// Conforms to: Production Standards, Clean MVC Architecture &amp; Zero Fabricated Data principles.
/// </summary>
[ApiController]
[Route("api/iot/thresholds")]
public class IotThresholdsController : ControllerBase
{
    private readonly IIotThresholdService _thresholdService;

    public IotThresholdsController(IIotThresholdService thresholdService)
    {
        _thresholdService = thresholdService;
    }

    /// <summary>
    /// Retrieves threshold configuration summary including devices, rules, and recipients.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetThresholdsSummary()
    {
        var summary = await _thresholdService.GetConfigSummaryAsync();
        return Ok(new
        {
            success = true,
            data = summary
        });
    }

    /// <summary>
    /// Retrieves threshold configuration for a specific device ID.
    /// </summary>
    [HttpGet("{deviceId}")]
    public async Task<IActionResult> GetThreshold(string deviceId)
    {
        var item = await _thresholdService.GetThresholdByDeviceIdAsync(deviceId);
        if (item == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"IoT device '{deviceId}' not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = item
        });
    }

    /// <summary>
    /// Updates low/high alert thresholds and telemetry intervals for an individual device.
    /// </summary>
    [HttpPut("{deviceId}")]
    public async Task<IActionResult> UpdateThreshold(string deviceId, [FromBody] UpdateIotThresholdRequestDto request)
    {
        if (request.LowAlert >= request.HighAlert)
        {
            return BadRequest(new
            {
                success = false,
                message = "Low alert threshold must be strictly lower than high alert threshold."
            });
        }

        var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Manager";
        var updated = await _thresholdService.UpdateThresholdAsync(deviceId, request, userName);
        if (updated == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"IoT device '{deviceId}' not found."
            });
        }

        return Ok(new
        {
            success = true,
            message = $"Threshold for device '{deviceId}' updated successfully.",
            data = updated
        });
    }

    /// <summary>
    /// Batch updates alert thresholds across multiple IoT devices ("Save all").
    /// </summary>
    [HttpPost("batch")]
    public async Task<IActionResult> BatchUpdateThresholds([FromBody] BatchUpdateIotThresholdsRequestDto request)
    {
        var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Manager";
        var success = await _thresholdService.BatchUpdateThresholdsAsync(request, userName);
        if (!success)
        {
            return BadRequest(new
            {
                success = false,
                message = "Invalid threshold batch payload."
            });
        }

        var summary = await _thresholdService.GetConfigSummaryAsync();
        return Ok(new
        {
            success = true,
            message = "All IoT threshold configurations saved successfully.",
            data = summary
        });
    }

    /// <summary>
    /// Resets all IoT alert thresholds to standard regulatory defaults.
    /// </summary>
    [HttpPost("reset-defaults")]
    public async Task<IActionResult> ResetDefaults()
    {
        var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Manager";
        await _thresholdService.ResetDefaultsAsync(userName);
        var summary = await _thresholdService.GetConfigSummaryAsync();

        return Ok(new
        {
            success = true,
            message = "All IoT thresholds have been restored to regulatory defaults.",
            data = summary
        });
    }

    /// <summary>
    /// Retrieves list of designated alert notification recipients.
    /// </summary>
    [HttpGet("recipients")]
    public async Task<IActionResult> GetRecipients()
    {
        var list = await _thresholdService.GetAllRecipientsAsync();
        return Ok(new
        {
            success = true,
            data = list
        });
    }

    /// <summary>
    /// Adds a new personnel recipient to the IoT alert escalation chain.
    /// </summary>
    [HttpPost("recipients")]
    public async Task<IActionResult> AddRecipient([FromBody] AddIotAlertRecipientRequestDto request)
    {
        var created = await _thresholdService.AddRecipientAsync(request);
        return Created($"/api/iot/thresholds/recipients/{created.Id}", new
        {
            success = true,
            message = $"Alert recipient '{created.FullName}' added successfully.",
            data = created
        });
    }

    /// <summary>
    /// Removes a personnel recipient from the IoT alert escalation chain.
    /// </summary>
    [HttpDelete("recipients/{recipientId}")]
    public async Task<IActionResult> RemoveRecipient(string recipientId)
    {
        var success = await _thresholdService.RemoveRecipientAsync(recipientId);
        if (!success)
        {
            return NotFound(new
            {
                success = false,
                message = $"Recipient '{recipientId}' not found."
            });
        }

        return Ok(new
        {
            success = true,
            message = "Alert recipient removed successfully."
        });
    }
}
