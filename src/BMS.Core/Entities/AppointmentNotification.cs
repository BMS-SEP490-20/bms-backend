using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Type of appointment notification trigger.
/// </summary>
public enum NotificationType
{
    BookingConfirmed = 1,
    FastingReminder = 2,
    ScheduleReminder = 3,
    CancellationNotice = 4
}

/// <summary>
/// Delivery channel for communication dispatch.
/// </summary>
public enum NotificationChannel
{
    Email = 1,
    Push = 2,
    InApp = 3,
    Sms = 4
}

/// <summary>
/// Represents an appointment-related notification record (UC22).
/// Stored in MongoDB collection "appointment_notifications".
/// </summary>
public class AppointmentNotification
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("appointmentId")]
    public string AppointmentId { get; set; } = string.Empty;

    [BsonElement("appointmentCode")]
    public string AppointmentCode { get; set; } = string.Empty;

    [BsonElement("patientId")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("recipientEmail")]
    public string? RecipientEmail { get; set; }

    [BsonElement("recipientPhone")]
    public string? RecipientPhone { get; set; }

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    [BsonElement("type")]
    public NotificationType Type { get; set; } = NotificationType.BookingConfirmed;

    [BsonElement("channel")]
    public NotificationChannel Channel { get; set; } = NotificationChannel.Email;

    [BsonElement("isSent")]
    public bool IsSent { get; set; } = true;

    [BsonElement("sentAt")]
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    [BsonElement("isRead")]
    public bool IsRead { get; set; } = false;

    [BsonElement("fastingInstructions")]
    public string? FastingInstructions { get; set; }
}
