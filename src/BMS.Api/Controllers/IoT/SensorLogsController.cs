using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.IoT;
using BMS.Application.Interfaces;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Configuration;
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
    private readonly IIotThresholdService? _thresholdService;
    private readonly IIotSensorLogRepository? _logRepository;
    private readonly IIotThresholdRepository? _thresholdRepository;
    private readonly IConfiguration? _configuration;

    public SensorLogsController(
        IIotTelemetryService telemetryService,
        IIotThresholdService? thresholdService = null,
        IIotSensorLogRepository? logRepository = null,
        IIotThresholdRepository? thresholdRepository = null,
        IConfiguration? configuration = null)
    {
        _telemetryService = telemetryService;
        _thresholdService = thresholdService;
        _logRepository = logRepository;
        _thresholdRepository = thresholdRepository;
        _configuration = configuration;
    }

    public sealed class CreateIotDeviceRequest
    {
        public string DeviceId { get; set; } = string.Empty;
        public string? DeviceName { get; set; }
        public string? Location { get; set; }
        public double MinTemp { get; set; } = 2.0;
        public double MaxTemp { get; set; } = 8.0;
        public double MinHumidity { get; set; } = 45.0;
        public double MaxHumidity { get; set; } = 75.0;
        public int IntervalSeconds { get; set; } = 5;
    }

    public sealed class UpdateMqttConfigRequest
    {
        public string BrokerHost { get; set; } = "broker.emqx.io";
        public int BrokerPort { get; set; } = 1883;
        public string TopicPrefix { get; set; } = "bms/sep490/iot/hkd-lab01";
        public string DefaultDeviceId { get; set; } = "BMS-IOT-001";
    }

    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpPost("devices")]
    public async Task<IActionResult> CreateDevice([FromBody] CreateIotDeviceRequest request)
    {
        if (_thresholdRepository == null || request == null || string.IsNullOrWhiteSpace(request.DeviceId))
        {
            return BadRequest(new { success = false, message = "Mã thiết bị IoT không được để trống." });
        }

        var deviceId = request.DeviceId.Trim();
        var location = string.IsNullOrWhiteSpace(request.Location) ? "Blood Storage Room 01" : request.Location.Trim();
        var deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? $"ESP32 DHT22 ({deviceId})" : request.DeviceName.Trim();

        await _thresholdRepository.UpsertThresholdAsync(new BMS.Core.Entities.IotDeviceThreshold
        {
            DeviceId = deviceId,
            DeviceName = $"{deviceName} (Nhiệt độ)",
            Location = location,
            SensorType = "Temperature",
            Unit = "°C",
            LowAlert = request.MinTemp,
            HighAlert = request.MaxTemp,
            IntervalSeconds = Math.Clamp(request.IntervalSeconds, 3, 300),
            Status = "Active"
        });

        await _thresholdRepository.UpsertThresholdAsync(new BMS.Core.Entities.IotDeviceThreshold
        {
            DeviceId = $"{deviceId}-HUM",
            DeviceName = $"{deviceName} (Độ ẩm)",
            Location = location,
            SensorType = "Humidity",
            Unit = "%",
            LowAlert = request.MinHumidity,
            HighAlert = request.MaxHumidity,
            IntervalSeconds = Math.Clamp(request.IntervalSeconds, 3, 300),
            Status = "Active"
        });

        return Ok(new
        {
            success = true,
            message = $"Đã thêm thiết bị '{deviceId}' vào hệ thống giám sát.",
            data = new { id = deviceId, deviceId, deviceName, location, label = $"{deviceId} — {location}" }
        });
    }

    [Authorize(Roles = RoleSets.AdminManagerLab)]
    [HttpGet("mqtt-config")]
    public async Task<IActionResult> GetMqttConfig(CancellationToken ct)
    {
        var dbConfig = _logRepository != null ? await _logRepository.GetMqttConfigAsync(ct) : null;
        var brokerHost = dbConfig?.BrokerHost ?? _configuration?["IoT:MqttBrokerHost"] ?? "broker.emqx.io";
        var brokerPort = dbConfig?.BrokerPort > 0 ? dbConfig.BrokerPort : (int.TryParse(_configuration?["IoT:MqttBrokerPort"], out var p) ? p : 1883);
        var topicPrefix = (dbConfig?.TopicPrefix ?? _configuration?["IoT:MqttTopicPrefix"] ?? "bms/sep490/iot/hkd-lab01").TrimEnd('/');
        var defaultDeviceId = dbConfig?.DefaultDeviceId ?? _configuration?["IoT:DefaultDeviceId"] ?? "BMS-IOT-001";

        return Ok(new
        {
            success = true,
            data = new
            {
                brokerHost,
                brokerPort,
                topicPrefix,
                defaultDeviceId,
                isBridgeConnected = BMS.Api.Services.IotMqttBridgeHostedService.IsConnected,
                updatedAt = dbConfig?.UpdatedAt
            }
        });
    }

    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpPut("mqtt-config")]
    public async Task<IActionResult> UpdateMqttConfig([FromBody] UpdateMqttConfigRequest request, CancellationToken ct)
    {
        if (_logRepository == null || request == null || string.IsNullOrWhiteSpace(request.BrokerHost) || string.IsNullOrWhiteSpace(request.TopicPrefix))
        {
            return BadRequest(new { success = false, message = "Thông tin Broker Host và Kênh MQTT không được để trống." });
        }

        var config = new BMS.Core.Entities.IotMqttConfig
        {
            Id = "default",
            BrokerHost = request.BrokerHost.Trim(),
            BrokerPort = request.BrokerPort > 0 ? request.BrokerPort : 1883,
            TopicPrefix = request.TopicPrefix.Trim().TrimEnd('/'),
            DefaultDeviceId = string.IsNullOrWhiteSpace(request.DefaultDeviceId) ? "BMS-IOT-001" : request.DefaultDeviceId.Trim(),
            UpdatedBy = User?.Identity?.Name ?? "Manager"
        };

        await _logRepository.UpsertMqttConfigAsync(config, ct);
        BMS.Api.Services.IotMqttBridgeHostedService.RequestReconnect();

        return Ok(new
        {
            success = true,
            message = "Đã lưu cấu hình kênh MQTT và yêu cầu kết nối lại ngay lập tức.",
            data = config
        });
    }

    /// <summary>
    /// Lists all configured IoT monitoring devices from the database without hardcoded lists.
    /// </summary>
    [Authorize(Roles = RoleSets.AdminManagerLab)]
    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices()
    {
        if (_thresholdService == null)
        {
            return Ok(new { success = true, data = Array.Empty<object>() });
        }

        var thresholds = await _thresholdService.GetAllThresholdsAsync();
        var devices = thresholds
            .Where(t => !string.IsNullOrWhiteSpace(t.DeviceId) && !t.DeviceId.EndsWith("-HUM", StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new
                {
                    id = first.DeviceId,
                    deviceId = first.DeviceId,
                    deviceName = first.DeviceName,
                    location = first.Location,
                    sensorType = first.SensorType,
                    status = first.Status,
                    label = $"{first.DeviceId} — {first.Location}"
                };
            })
            .ToList();

        return Ok(new
        {
            success = true,
            data = devices,
            total = devices.Count
        });
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
    [HttpGet("summaries")]
    [HttpGet("daily-summaries")]
    [Authorize(Roles = RoleSets.AdminManagerLab)]
    public async Task<IActionResult> GetDailySummaries(
        [FromQuery] string? deviceId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int maxLimit = 365,
        CancellationToken ct = default)
    {
        var summaries = await _telemetryService.GetDailySummariesAsync(deviceId, fromDate, toDate, maxLimit, ct);
        return Ok(new
        {
            success = true,
            data = summaries,
            total = summaries.Count
        });
    }

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
