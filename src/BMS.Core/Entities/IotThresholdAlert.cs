using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Urgency severity levels for IoT threshold violation alarms (UC68).
/// </summary>
public enum IotAlertSeverity
{
    Warning = 1,          // Minor excursion (e.g. +0.5°C over threshold)
    Critical = 2,         // Serious excursion (e.g. +2.0°C over threshold)
    Danger_Emergency = 3  // Severe risk of specimen spoilage (e.g. > +3.0°C or power failure)
}

/// <summary>
/// Lifecycle status of an IoT environmental threshold alarm.
/// </summary>
public enum IotAlertStatus
{
    Active = 1,
    Acknowledged = 2,
    Resolved = 3
}

/// <summary>
/// IoT Environmental Threshold Violation Alert entity (UC68 / Alert Threshold Violation).
/// Maps to MongoDB collection "iot_alerts".
/// </summary>
[BsonIgnoreExtraElements]
public class IotThresholdAlert
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("device_id")]
    public string DeviceId { get; set; } = string.Empty; // e.g. TEMP-001, HUM-002

    [BsonElement("device_name")]
    public string DeviceName { get; set; } = string.Empty;

    [BsonElement("location")]
    public string Location { get; set; } = string.Empty; // e.g. Tủ lưu trữ máu R-01

    [BsonElement("sensor_type")]
    public string SensorType { get; set; } = "Temperature"; // "Temperature", "Humidity"

    [BsonElement("unit")]
    public string Unit { get; set; } = "°C";

    [BsonElement("observed_value")]
    public double ObservedValue { get; set; }

    [BsonElement("low_alert")]
    public double LowAlert { get; set; }

    [BsonElement("high_alert")]
    public double HighAlert { get; set; }

    [BsonElement("excursion_amount")]
    public double ExcursionAmount { get; set; }

    [BsonElement("severity")]
    public string SeverityString { get; set; } = IotAlertSeverity.Warning.ToString();

    [BsonIgnore]
    public IotAlertSeverity Severity
    {
        get => Enum.TryParse<IotAlertSeverity>(SeverityString, true, out var s) ? s : IotAlertSeverity.Warning;
        set => SeverityString = value.ToString();
    }

    [BsonElement("status")]
    public string StatusString { get; set; } = IotAlertStatus.Active.ToString();

    [BsonIgnore]
    public IotAlertStatus Status
    {
        get => Enum.TryParse<IotAlertStatus>(StatusString, true, out var s) ? s : IotAlertStatus.Active;
        set => StatusString = value.ToString();
    }

    [BsonElement("triggered_at")]
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;

    [BsonElement("acknowledged_at")]
    public DateTime? AcknowledgedAt { get; set; }

    [BsonElement("acknowledged_by")]
    public string? AcknowledgedBy { get; set; }

    [BsonElement("resolved_at")]
    public DateTime? ResolvedAt { get; set; }

    [BsonElement("resolved_by")]
    public string? ResolvedBy { get; set; }

    [BsonElement("resolution_notes")]
    public string? ResolutionNotes { get; set; }

    [BsonElement("notification_channels_dispatched")]
    public List<string> NotificationChannelsDispatched { get; set; } = new();

    [BsonElement("duration_minutes")]
    public double DurationMinutes { get; set; }
}
