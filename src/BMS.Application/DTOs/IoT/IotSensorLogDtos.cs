using System;
using System.Collections.Generic;

namespace BMS.Application.DTOs.IoT;

/// <summary>
/// Buffered sensor measurement captured while ESP32 was disconnected from Backend.
/// </summary>
public class BufferedTelemetryItemDto
{
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    /// <summary>
    /// Seconds elapsed between when the ESP32 captured this sample in RAM and when it was transmitted to Backend.
    /// </summary>
    public int AgeSeconds { get; set; }
    public DateTime? RecordedAt { get; set; }
}

/// <summary>
/// Incoming telemetry payload from ESP32 IoT sensor gateway (UC65 Collect Temperature &amp; UC66 Collect Humidity).
/// Matches the JSON format transmitted by <c>Arduino/WifiTest/WifiTest.ino</c>.
/// </summary>
public class IngestIotTelemetryRequestDto
{
    public string DeviceId { get; set; } = "BMS-IOT-001";
    public string? DeviceName { get; set; }
    public string Location { get; set; } = "Blood Storage Room 01";
    public double? Temperature { get; set; }
    public double? Humidity { get; set; }
    public bool IsBuffered { get; set; }
    public int? AgeSeconds { get; set; }
    public DateTime? Timestamp { get; set; }
    public List<BufferedTelemetryItemDto>? BufferedReadings { get; set; }
}

/// <summary>
/// Response returned to ESP32 and clients after ingesting telemetry, including 2-way threshold synchronization (UC67 -> ESP32).
/// </summary>
public class IotTelemetryIngestResponseDto
{
    public bool Success { get; set; } = true;
    public string DeviceId { get; set; } = "BMS-IOT-001";
    public string Location { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public string Status { get; set; } = "Normal";
    public bool IsViolation { get; set; }
    public DateTime RecordedAt { get; set; }
    public int BufferedSyncedCount { get; set; }
    public bool ReconnectedFromOutage { get; set; }
    public string? OutageId { get; set; }

    // 2-Way Threshold Synchronization fields consumed by ESP32 firmware
    public double MinTemp { get; set; } = 2.0;
    public double MaxTemp { get; set; } = 8.0;
    public double MinHumidity { get; set; } = 45.0;
    public double MaxHumidity { get; set; } = 75.0;
    public int IntervalSeconds { get; set; } = 5;

    public string Message { get; set; } = string.Empty;
    public IotAlertDto? TriggeredAlert { get; set; }
}

/// <summary>
/// Time-series sensor log item DTO (UC69 View Temperature Logs).
/// </summary>
public class IotSensorLogDto
{
    public string Id { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public double TemperatureC { get; set; }
    public double HumidityPercent { get; set; }
    public double MinTempAllowed { get; set; }
    public double MaxTempAllowed { get; set; }
    public double MinHumidityAllowed { get; set; }
    public double MaxHumidityAllowed { get; set; }
    public string Status { get; set; } = "Normal";
    public bool IsViolation { get; set; }
    public bool IsBuffered { get; set; }
    public string? OutageId { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime ReceivedAt { get; set; }
}

/// <summary>
/// Connection outage record DTO representing a period when ESP32 and Backend lost connection.
/// </summary>
public class IotConnectionOutageDto
{
    public string Id { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime DisconnectedAt { get; set; }
    public DateTime? ReconnectedAt { get; set; }
    public double DurationSeconds { get; set; }
    public int BufferedRecordsSynced { get; set; }
    public string Status { get; set; } = "Disconnected";
    public string? Notes { get; set; }
}

/// <summary>
/// Live connectivity health status of the IoT sensor gateway.
/// </summary>
public class IotConnectionStatusDto
{
    public string DeviceId { get; set; } = "BMS-IOT-001";
    public string DeviceName { get; set; } = "ESP32 DHT22 Tủ Bảo Quản Máu 01";
    public string Location { get; set; } = "Blood Storage Room 01";
    public bool IsConnected { get; set; }
    public string ConnectionState { get; set; } = "DISCONNECTED"; // "CONNECTED" | "DISCONNECTED"
    public DateTime? LastSeenAt { get; set; }
    public double? SecondsSinceLastSeen { get; set; }
    public int TimeoutThresholdSeconds { get; set; } = 15;
    public DateTime? DisconnectedSince { get; set; }
    public double CurrentOutageSeconds { get; set; }
    public string? AlertCode { get; set; }
    public string? AlertMessage { get; set; }
    public double? LatestTemperature { get; set; }
    public double? LatestHumidity { get; set; }
    public string? LatestStatus { get; set; }
    public IotConnectionOutageDto? ActiveOutage { get; set; }
    public IotConnectionOutageDto? LastRecoveredOutage { get; set; }
}

/// <summary>
/// Query filter parameters for viewing IoT temperature &amp; humidity logs (UC69).
/// </summary>
public class IotSensorLogQueryDto
{
    public string? DeviceId { get; set; }
    public string? Status { get; set; } // "All", "Normal", "Violation", "Buffered"
    public string? OutageId { get; set; }
    public string? Search { get; set; }
    public int? Hours { get; set; } = 24;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// Comprehensive dashboard &amp; log response combining live status, KPIs, time-series chart points, outage windows, and paginated logs.
/// </summary>
public class IotMonitoringDashboardDto
{
    public IotConnectionStatusDto Connection { get; set; } = new();
    public double MinTempThreshold { get; set; } = 2.0;
    public double MaxTempThreshold { get; set; } = 8.0;
    public double MinHumidityThreshold { get; set; } = 45.0;
    public double MaxHumidityThreshold { get; set; } = 75.0;
    public int IntervalSeconds { get; set; } = 5;

    // Statistical KPIs
    public double? LatestTemperature { get; set; }
    public double? LatestHumidity { get; set; }
    public double MinTemperature { get; set; }
    public double MaxTemperature { get; set; }
    public double AvgTemperature { get; set; }
    public double MeanKineticTemperature { get; set; }
    public double MinHumidity { get; set; }
    public double MaxHumidity { get; set; }
    public double AvgHumidity { get; set; }
    public int TotalLogsCount { get; set; }
    public int ViolationLogsCount { get; set; }
    public int BufferedLogsCount { get; set; }
    public int OutagesCount { get; set; }

    // Chronological chart points (oldest -> newest)
    public List<IotSensorLogDto> ChartSeries { get; set; } = new();
    // Outage windows for chart shading & history list
    public List<IotConnectionOutageDto> Outages { get; set; } = new();
    // Paginated table items (newest -> oldest)
    public List<IotSensorLogDto> Logs { get; set; } = new();
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// Request payload for simulating or recovering a disconnection outage with buffered readings.
/// </summary>
public class SimulateReconnectRequestDto
{
    public string DeviceId { get; set; } = "BMS-IOT-001";
    public string Location { get; set; } = "Blood Storage Room 01";
    public int OutageDurationSeconds { get; set; } = 30;
    public int SampleIntervalSeconds { get; set; } = 5;
    public double BaseTemperature { get; set; } = 4.6;
    public double BaseHumidity { get; set; } = 56.0;
}


/// <summary>
/// Daily rolled-up environmental summary DTO for multi-year cold-chain reporting.
/// </summary>
public class IotDailySummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime SummaryDate { get; set; }
    public double MinTemp { get; set; }
    public double MaxTemp { get; set; }
    public double AvgTemp { get; set; }
    public double MktTemp { get; set; }
    public double MinHumidity { get; set; }
    public double MaxHumidity { get; set; }
    public double AvgHumidity { get; set; }
    public int SampleCount { get; set; }
    public int ViolationCount { get; set; }
    public int OutageCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}
