using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Time-series environmental telemetry log for cold-storage refrigerators and laboratory rooms (UC65, UC66, UC69).
/// Maps to MongoDB collection <c>iot_temperature_logs</c>.
/// </summary>
[BsonIgnoreExtraElements]
public class IotSensorLog
{
    [BsonId]
    [BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("device_id")]
    public string DeviceId { get; set; } = "BMS-IOT-001";

    [BsonElement("device_name")]
    public string DeviceName { get; set; } = "ESP32 DHT22 Tủ Bảo Quản Máu 01";

    [BsonElement("location")]
    public string Location { get; set; } = "Blood Storage Room 01";

    [BsonElement("temperature_c")]
    public double TemperatureC { get; set; }

    [BsonElement("humidity_percent")]
    public double HumidityPercent { get; set; }

    [BsonElement("min_temp_allowed")]
    public double MinTempAllowed { get; set; } = 2.0;

    [BsonElement("max_temp_allowed")]
    public double MaxTempAllowed { get; set; } = 8.0;

    [BsonElement("min_humidity_allowed")]
    public double MinHumidityAllowed { get; set; } = 45.0;

    [BsonElement("max_humidity_allowed")]
    public double MaxHumidityAllowed { get; set; } = 75.0;

    /// <summary>
    /// Evaluation status: Normal, Alert_High_Temp, Alert_Low_Temp, Alert_High_Humidity, Alert_Low_Humidity.
    /// </summary>
    [BsonElement("status")]
    public string Status { get; set; } = "Normal";

    /// <summary>
    /// Indicates whether this reading was buffered in ESP32 memory during a connection loss and synced upon reconnection.
    /// </summary>
    [BsonElement("is_buffered")]
    public bool IsBuffered { get; set; }

    /// <summary>
    /// Optional reference to the connection outage incident during which this reading was buffered.
    /// </summary>
    [BsonElement("outage_id")]
    [BsonIgnoreIfNull]
    public string? OutageId { get; set; }

    /// <summary>
    /// Actual UTC timestamp when the sensor captured the measurement.
    /// </summary>
    [BsonElement("recorded_at")]
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the backend server persisted the measurement.
    /// </summary>
    [BsonElement("received_at")]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Records a period during which an IoT sensor device lost connectivity with the BMS Backend.
/// Tracks buffered telemetry synced once connectivity is restored.
/// Maps to MongoDB collection <c>iot_connection_outages</c>.
/// </summary>
[BsonIgnoreExtraElements]
public class IotConnectionOutage
{
    [BsonId]
    [BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
    public string Id { get; set; } = $"OUTAGE-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";

    [BsonElement("device_id")]
    public string DeviceId { get; set; } = "BMS-IOT-001";

    [BsonElement("device_name")]
    public string DeviceName { get; set; } = "ESP32 DHT22 Tủ Bảo Quản Máu 01";

    [BsonElement("location")]
    public string Location { get; set; } = "Blood Storage Room 01";

    [BsonElement("disconnected_at")]
    public DateTime DisconnectedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("reconnected_at")]
    [BsonIgnoreIfNull]
    public DateTime? ReconnectedAt { get; set; }

    [BsonElement("duration_seconds")]
    public double DurationSeconds { get; set; }

    [BsonElement("buffered_records_synced")]
    public int BufferedRecordsSynced { get; set; }

    /// <summary>
    /// Outage lifecycle state: "Disconnected" (active outage) or "Reconnected" (resolved).
    /// </summary>
    [BsonElement("status")]
    public string Status { get; set; } = "Disconnected";

    [BsonElement("notes")]
    [BsonIgnoreIfNull]
    public string? Notes { get; set; }
}
