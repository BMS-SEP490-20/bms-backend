using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Domain entity representing a clinical push notification record and audit log (UC58 / Send Push Notifications).
/// Maps to MongoDB collection "notification_logs".
/// Conforms to: Production Engineering Standards & AGENTS-DESIGN.md.
/// </summary>
[BsonIgnoreExtraElements]
public class PushNotificationLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("notification_id")]
    public string NotificationId { get; set; } = string.Empty;

    [BsonElement("recipient_user_id")]
    public string RecipientUserId { get; set; } = string.Empty;

    [BsonElement("recipient_role")]
    public string? RecipientRole { get; set; }

    [BsonElement("recipient_device_token")]
    public string? RecipientDeviceToken { get; set; }

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    [BsonElement("channel")]
    public string Channel { get; set; } = "ALL"; // PUSH_FCM, IN_APP_WEB, SMS, EMAIL, ALL

    [BsonElement("priority")]
    public string Priority { get; set; } = "NORMAL"; // NORMAL, HIGH, CRITICAL_PANIC

    [BsonElement("action_url")]
    public string? ActionUrl { get; set; }

    [BsonElement("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [BsonElement("status")]
    public string Status { get; set; } = "PENDING"; // PENDING, DELIVERED, FAILED, READ

    [BsonElement("retry_count")]
    public int RetryCount { get; set; } = 0;

    [BsonElement("dispatched_at")]
    public DateTime DispatchedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("delivered_at")]
    public DateTime? DeliveredAt { get; set; }

    [BsonElement("read_at")]
    public DateTime? ReadAt { get; set; }

    [BsonElement("failure_reason")]
    public string? FailureReason { get; set; }
}

/// <summary>
/// Device registration token mapping for FCM and Web push.
/// </summary>
[BsonIgnoreExtraElements]
public class DeviceTokenRegistration
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("user_id")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("device_token")]
    public string DeviceToken { get; set; } = string.Empty;

    [BsonElement("platform")]
    public string Platform { get; set; } = "Android"; // Android, iOS, Web

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
