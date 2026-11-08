using System;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.IoT;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.IoT;

/// <summary>
/// Controller ingesting and querying IoT environmental sensor telemetry logs (UC65, UC66, UC69 / Iteration 3).
/// Connects directly with ESP32 DHT22 hardware (<c>Arduino/WifiTest/WifiTest.ino</c>) at <c>/api/iot/temperature</c>,
/// tracks connection loss outages, synchronizes offline buffered data, and returns 2-way threshold configurations.
/// Conforms to: BR-IOT-01, BR-IOT-02, MSG-ERR-03 &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/iot")]
[Authorize]
public class SensorLogsController : ControllerBase
{
    private readonly IIotTelemetryService _telemetryService;

    public SensorLogsController(IIotTelemetryService telemetryService)
    {
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// Ingests temperature and humidity telemetry from ESP32 IoT gateway (UC65 &amp; UC66),
    /// flushes offline buffered readings after reconnection, and returns active threshold bounds for 2-way sync.
    /// </summary>
    // Hardware uses X-Device-Key; staff simulation requires an authorized JWT role.
    [AllowAnonymous]
    [RequireDeviceKey]
    [HttpPost("temperature")]
    [HttpPost("humidity")]
    [HttpPost("telemetry")]
    [HttpPost("")]
    public async Task<IActionResult> IngestTelemetry([FromBody] IngestIotTelemetryRequestDto request, CancellationToken ct)
    {
        if (request == null || (!request.Temperature.HasValue && !request.Humidity.HasValue))
        {
            return BadRequest(new
            {
                success = false,
                message = "Dữ liệu cảm biến không hợp lệ. Cần có ít nhất nhiệt độ (temperature) hoặc độ ẩm (humidity)."
            });
        }

        var result = await _telemetryService.IngestTelemetryAsync(request, ct);
        return Ok(new
        {
            success = true,
            deviceId = result.DeviceId,
            location = result.Location,
            temperature = result.Temperature,
            humidity = result.Humidity,
            status = result.Status,
            isViolation = result.IsViolation,
            recordedAt = result.RecordedAt,
            bufferedSyncedCount = result.BufferedSyncedCount,
            reconnectedFromOutage = result.ReconnectedFromOutage,
            outageId = result.OutageId,
            minTemp = result.MinTemp,
            maxTemp = result.MaxTemp,
            minHumidity = result.MinHumidity,
            maxHumidity = result.MaxHumidity,
            intervalSeconds = result.IntervalSeconds,
            message = result.Message,
            data = result
        });
    }

    /// <summary>
    /// Queries time-series temperature and humidity logs, chart series, connection status, and outage intervals (UC65, UC66, UC69).
    /// </summary>
    // WHY: UC65/UC66/UC69 view restricted to Lab, Manager, Admin (ROLE-PERMISSION-MATRIX §1).
    [Authorize(Roles = RoleSets.AdminManagerLab)]
    [HttpGet]
    [HttpGet("temperature")]
    [HttpGet("humidity")]
    [HttpGet("logs")]
    public async Task<IActionResult> GetSensorLogs([FromQuery] IotSensorLogQueryDto query, CancellationToken ct)
    {
        var dashboard = await _telemetryService.GetMonitoringDashboardAsync(query, ct);
        return Ok(new
        {
            success = true,
            data = dashboard,
            total = dashboard.TotalLogsCount,
            message = "Đã tải dữ liệu giám sát nhiệt độ, độ ẩm và lịch sử cảm biến IoT thành công."
        });
    }

    /// <summary>
    /// Retrieves real-time connectivity status of the ESP32 IoT sensor gateway and any active disconnection alert (MSG-ERR-03).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("status")]
    public async Task<IActionResult> GetConnectionStatus([FromQuery] string? deviceId, CancellationToken ct)
    {
        var status = await _telemetryService.GetConnectionStatusAsync(deviceId, ct);
        return Ok(new
        {
            success = true,
            data = status
        });
    }

    /// <summary>
    /// Retrieves history of connection loss periods and buffered data recovery.
    /// </summary>
    [Authorize(Roles = RoleSets.AdminManagerLab)]
    [HttpGet("outages")]
    public async Task<IActionResult> GetOutages([FromQuery] string? deviceId, CancellationToken ct)
    {
        var outages = await _telemetryService.GetOutagesAsync(deviceId, ct);
        return Ok(new
        {
            success = true,
            data = outages,
            total = outages.Count
        });
    }

    /// <summary>
    /// Marks a device as disconnected for testing/drilling connection loss alarms (MSG-ERR-03).
    /// </summary>
    [Authorize(Roles = RoleSets.AdminManagerLab)]
    [HttpPost("disconnect")]
    public async Task<IActionResult> ForceDisconnect([FromQuery] string deviceId = "BMS-IOT-001", CancellationToken ct = default)
    {
        var status = await _telemetryService.ForceDisconnectStateAsync(deviceId, "Ngắt kết nối kiểm thử cảnh báo mất kết nối IoT.", ct);
        return Ok(new
        {
            success = true,
            data = status,
            message = status.AlertMessage
        });
    }

    /// <summary>
    /// Reconnects and flushes buffered offline telemetry readings to verify outage window recovery.
    /// </summary>
    [Authorize(Roles = RoleSets.AdminManagerLab)]
    [HttpPost("reconnect-sync")]
    public async Task<IActionResult> ReconnectAndSync([FromBody] SimulateReconnectRequestDto request, CancellationToken ct = default)
    {
        var result = await _telemetryService.SimulateReconnectWithBufferedDataAsync(request ?? new SimulateReconnectRequestDto(), ct);
        return Ok(new
        {
            success = true,
            data = result,
            message = $"Đã kết nối lại và đồng bộ bù {result.BufferedSyncedCount} bản ghi trong khoảng thời gian mất kết nối."
        });
    }
}
