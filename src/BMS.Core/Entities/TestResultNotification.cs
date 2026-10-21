using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Channel through which the test result notification is dispatched.
/// </summary>
public enum TestResultNotificationChannel
{
    InApp = 1,
    Sms = 2,
    Email = 3,
    Push = 4,
    All = 5
}

/// <summary>
/// Urgency and priority level of the notification.
/// </summary>
public enum NotificationPriority
{
    Normal = 1,
    High = 2,
    Urgent = 3,
    Critical = 4
}

/// <summary>
/// Transmission and delivery status of the notification.
/// </summary>
public enum NotificationDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Delivered = 3,
    Failed = 4,
    Read = 5
}

/// <summary>
/// Domain Entity representing a Laboratory Diagnostic Test Result Notification (UC60 / BR-182).
/// Supports multi-channel dispatch (In-App, SMS, Email, Push) and Panic/Critical Value Escalation.
/// </summary>
public class TestResultNotification
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string NotificationId { get; set; } = string.Empty;

    public string TestOrderId { get; set; } = string.Empty;

    public string? SpecimenBarcode { get; set; }

    public string PatientId { get; set; } = string.Empty;

    public string PatientName { get; set; } = string.Empty;

    public string? PatientPhone { get; set; }

    public string? PatientEmail { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public TestResultNotificationChannel Channel { get; set; } = TestResultNotificationChannel.InApp;

    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;

    /// <summary>
    /// Flags whether this result contains critical/panic values requiring urgent clinical intervention.
    /// </summary>
    public bool IsCriticalPanicValue { get; set; }

    /// <summary>
    /// List of abnormal/panic parameters (e.g. "Glucose: 28.5 mmol/L (Ngưỡng cao nguy hiểm)").
    /// </summary>
    public List<string> CriticalParameters { get; set; } = [];

    /// <summary>
    /// Flags if doctor consultation or emergency department contact is required.
    /// </summary>
    public bool RequiresImmediateAction { get; set; }

    public string? ClinicalRecommendation { get; set; }

    public string? TestPackageName { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public DateTime? ReadAt { get; set; }

    public string? FailureReason { get; set; }

    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string CreatedBy { get; set; } = "SYSTEM";
}
