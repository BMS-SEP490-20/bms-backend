using BMS.Core.Entities;

namespace BMS.Application.DTOs.Appointment;

/// <summary>
/// Request payload to trigger notification dispatch for an appointment (UC22).
/// </summary>
public class SendNotificationRequestDto
{
    public NotificationChannel Channel { get; set; } = NotificationChannel.Email;
    public NotificationType Type { get; set; } = NotificationType.BookingConfirmed;
    public string? CustomMessage { get; set; }
}

/// <summary>
/// Read DTO for an appointment notification log item.
/// </summary>
public class AppointmentNotificationDto
{
    public string Id { get; set; } = string.Empty;
    public string AppointmentId { get; set; } = string.Empty;
    public string AppointmentCode { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string? RecipientEmail { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public bool IsSent { get; set; }
    public DateTime SentAt { get; set; }
    public bool IsRead { get; set; }
    public string? FastingInstructions { get; set; }
}
