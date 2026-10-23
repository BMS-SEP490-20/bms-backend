using System;
using System.Collections.Generic;

namespace BMS.Application.DTOs.IoT;

/// <summary>
/// Data Transfer Object representing an IoT Environmental Threshold Violation Alert (UC68).
/// </summary>
public class IotAlertDto
{
    public string Id { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string SensorType { get; set; } = "Temperature";
    public string Unit { get; set; } = "°C";
    public double ObservedValue { get; set; }
    public double LowAlert { get; set; }
    public double HighAlert { get; set; }
    public double ExcursionAmount { get; set; }
    public string Severity { get; set; } = "Warning";
    public string Status { get; set; } = "Active";
    public DateTime TriggeredAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionNotes { get; set; }
    public List<string> NotificationChannelsDispatched { get; set; } = new();
    public double DurationMinutes { get; set; }
}

/// <summary>
/// Query filter parameters for listing IoT alerts.
/// </summary>
public class IotAlertQueryParameters
{
    public string? Status { get; set; } // "Active", "Acknowledged", "Resolved", "All"
    public string? Severity { get; set; } // "Warning", "Critical", "Danger_Emergency", "All"
    public string? DeviceId { get; set; }
    public string? SensorType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Executive summary indicators for IoT alert surveillance.
/// </summary>
public class IotAlertSummaryDto
{
    public int ActiveAlertsCount { get; set; }
    public int CriticalEmergencyCount { get; set; }
    public int AcknowledgedCount { get; set; }
    public int ResolvedTodayCount { get; set; }
    public double AverageResolutionMinutes { get; set; }
    public string SystemHealthStatus { get; set; } = "NORMAL"; // "NORMAL", "WARNING", "CRITICAL"
}

/// <summary>
/// Operator acknowledgment payload.
/// </summary>
public class AcknowledgeAlertRequestDto
{
    public string OperatorName { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

/// <summary>
/// Corrective action & resolution payload.
/// </summary>
public class ResolveAlertRequestDto
{
    public string OperatorName { get; set; } = string.Empty;
    public string ResolutionNotes { get; set; } = string.Empty;
}

/// <summary>
/// Telemetry sensor reading packet for live threshold evaluation.
/// </summary>
public class TelemetryReadingDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string SensorType { get; set; } = "Temperature";
    public double Value { get; set; }
    public DateTime? Timestamp { get; set; }
}

/// <summary>
/// Result of evaluating a telemetry reading against configured thresholds.
/// </summary>
public class ThresholdEvaluationResultDto
{
    public bool IsViolation { get; set; }
    public double ExcursionAmount { get; set; }
    public string? Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public IotAlertDto? Alert { get; set; }
}

/// <summary>
/// Manual or simulation alert request payload.
/// </summary>
public class SimulateAlertRequestDto
{
    public string DeviceId { get; set; } = "TEMP-001";
    public string DeviceName { get; set; } = "Tủ lưu trữ máu toàn phần R-01";
    public string Location { get; set; } = "Kho lạnh bảo quản mẫu tầng 1";
    public string SensorType { get; set; } = "Temperature";
    public double SimulatedValue { get; set; } = 8.6;
    public double LowAlert { get; set; } = 2.0;
    public double HighAlert { get; set; } = 6.0;
}
