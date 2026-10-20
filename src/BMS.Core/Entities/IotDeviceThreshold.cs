using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// IoT Sensor Device Alert Threshold configuration entity (UC67 / Configure IoT Thresholds).
/// Maps to MongoDB collection "iot_thresholds".
/// </summary>
[BsonIgnoreExtraElements]
public class IotDeviceThreshold
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("device_id")]
    public string DeviceId { get; set; } = string.Empty; // e.g. TEMP-001, HUM-001

    [BsonElement("device_name")]
    public string DeviceName { get; set; } = string.Empty;

    [BsonElement("location")]
    public string Location { get; set; } = string.Empty; // e.g. Fridge R-01, Lab Room 01

    [BsonElement("sensor_type")]
    public string SensorType { get; set; } = "Temperature"; // "Temperature" | "Humidity"

    [BsonElement("unit")]
    public string Unit { get; set; } = "°C"; // "°C" | "%"

    [BsonElement("low_alert")]
    public double LowAlert { get; set; }

    [BsonElement("high_alert")]
    public double HighAlert { get; set; }

    [BsonElement("interval_seconds")]
    public int IntervalSeconds { get; set; } = 60;

    [BsonElement("status")]
    public string Status { get; set; } = "Active"; // "Active" | "Warning" | "Inactive"

    [BsonElement("last_updated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    [BsonElement("updated_by")]
    public string UpdatedBy { get; set; } = "System";
}

/// <summary>
/// IoT Threshold Violation Notification Recipient (UC67).
/// Maps to MongoDB collection "iot_recipients".
/// </summary>
[BsonIgnoreExtraElements]
public class IotAlertRecipient
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("full_name")]
    public string FullName { get; set; } = string.Empty;

    [BsonElement("role")]
    public string Role { get; set; } = string.Empty; // e.g. Manager, Cold storage lead

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("phone")]
    public string Phone { get; set; } = string.Empty;

    [BsonElement("channels")]
    public List<string> Channels { get; set; } = new() { "Push", "Email" };

    [BsonElement("is_active")]
    public bool IsActive { get; set; } = true;
}
