using BMS.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.IoT;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.IoT;

/// <summary>
/// Controller handling IoT threshold alarms, specimen cold-chain violations, and incident escalation (UC68 / Iteration 3 #I3-061).
/// Conforms to: Production Standards, ISO 15189 Quality Management & AGENTS-CODE.md.
/// </summary>
[ApiController]
// "api/[controller]" was removed: it resolves to api/Alerts, which is the same route as api/alerts below
// (routing is case-insensitive) and made every alerts request fail with AmbiguousMatchException (HTTP 500).
[Route("api/iot/alerts")]
[Route("api/v1/iot/alerts")]
[Route("api/alerts")]
[Route("api/v1/alerts")]
// WHY: UC68: view/acknowledge Lab, Manager; resolve + CRITICAL report Manager; simulate Admin (dev); telemetry = device key (C13). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class AlertsController : ControllerBase
{
    private readonly IIotAlertService _alertService;

    public AlertsController(IIotAlertService alertService)
    {
        _alertService = alertService;
    }

    /// <summary>
    /// Retrieves filtered list of IoT threshold violation alerts.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet]
    public async Task<IActionResult> GetAlerts([FromQuery] IotAlertQueryParameters parameters, CancellationToken ct)
    {
        var alerts = await _alertService.GetAlertsAsync(parameters, ct);
        return Ok(new
        {
            success = true,
            data = alerts,
            total = alerts.Count,
            message = "Đã tải danh sách cảnh báo vi phạm ngưỡng IoT thành công."
        });
    }

    /// <summary>
    /// Retrieves currently active IoT threshold violation alarms.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("active")]
    public async Task<IActionResult> GetActiveAlerts(CancellationToken ct)
    {
        var alerts = await _alertService.GetAlertsAsync(new IotAlertQueryParameters { Status = "Active" }, ct);
        return Ok(new
        {
            success = true,
            data = alerts,
            total = alerts.Count,
            message = "Đã tải danh sách cảnh báo đang kích hoạt (Active) thành công."
        });
    }

    /// <summary>
    /// Retrieves executive surveillance summary KPIs (active, critical, acknowledged, resolved today).
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("summary")]
    public async Task<IActionResult> GetAlertSummary(CancellationToken ct)
    {
        var summary = await _alertService.GetAlertSummaryAsync(ct);
        return Ok(new
        {
            success = true,
            data = summary,
            message = "Đã tải thống kê tổng hợp cảnh báo IoT thành công."
        });
    }

    /// <summary>
    /// Retrieves detailed record of an alert by ID.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAlertById(string id, CancellationToken ct)
    {
        var alert = await _alertService.GetAlertByIdAsync(id, ct);
        if (alert == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Không tìm thấy cảnh báo IoT với mã '{id}'."
            });
        }

        return Ok(new
        {
            success = true,
            data = alert
        });
    }

    /// <summary>
    /// Acknowledges an active alert, logging operator ownership.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpPut("{id}/acknowledge")]
    public async Task<IActionResult> AcknowledgeAlert(string id, [FromBody] AcknowledgeAlertRequestDto request, CancellationToken ct)
    {
        try
        {
            var updated = await _alertService.AcknowledgeAlertAsync(id, request, ct);
            return Ok(new
            {
                success = true,
                data = updated,
                message = $"Đã tiếp nhận xử lý cảnh báo '{id}' thành công."
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
    /// Resolves an alert, documenting corrective actions taken to restore environmental safety.
    /// </summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpPut("{id}/resolve")]
    public async Task<IActionResult> ResolveAlert(string id, [FromBody] ResolveAlertRequestDto request, CancellationToken ct)
    {
        try
        {
            var resolved = await _alertService.ResolveAlertAsync(id, request, ct);
            return Ok(new
            {
                success = true,
                data = resolved,
                message = $"Đã hoàn tất khắc phục sự cố cho cảnh báo '{id}'."
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
    /// Evaluates incoming sensor telemetry against thresholds and generates instant alarms if breached.
    /// </summary>
    [AllowAnonymous] // JWT not used: IoT devices authenticate with X-Device-Key (C13)
    [RequireDeviceKey]
    [HttpPost("~/api/iot/telemetry/evaluate")]
    [HttpPost("~/api/v1/iot/telemetry/evaluate")]
    [HttpPost("evaluate")]
    public async Task<IActionResult> EvaluateTelemetry([FromBody] TelemetryReadingDto reading, CancellationToken ct)
    {
        try
        {
            var result = await _alertService.EvaluateTelemetryAsync(reading, ct);
            return Ok(new
            {
                success = true,
                data = result,
                message = result.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Simulates a sensor excursion alarm for training drills and testing escalation alerts.
    /// </summary>
    // "~/api/iot/alerts/simulate" is already produced by "simulate" under the api/iot/alerts prefix; declaring it
    // twice made POST api/iot/alerts/simulate ambiguous.
    [Authorize(Roles = RoleNames.Admin)]
    [HttpPost("simulate")]
    public async Task<IActionResult> SimulateAlert([FromBody] SimulateAlertRequestDto request, CancellationToken ct)
    {
        var alert = await _alertService.TriggerSimulatedAlertAsync(request, ct);
        return Ok(new
        {
            success = true,
            data = alert,
            message = $"Đã kích hoạt giả lập cảnh báo vượt ngưỡng cho thiết bị '{alert.DeviceId}' thành công."
        });
    }
}