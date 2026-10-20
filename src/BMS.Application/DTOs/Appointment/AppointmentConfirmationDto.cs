using System;
using System.Collections.Generic;

namespace BMS.Application.DTOs.Appointment;

/// <summary>
/// Status of multi-channel notification dispatches for an appointment.
/// </summary>
public class NotificationDeliveryStatusDto
{
    public bool EmailSent { get; set; } = true;
    public string? EmailSentAt { get; set; }
    public bool PushSent { get; set; } = true;
    public string? PushSentAt { get; set; }
    public bool SmsConfigured { get; set; } = false;
}

/// <summary>
/// Chronological milestone item for appointment reminders (UC22).
/// </summary>
public class ReminderTimelineItemDto
{
    public string Title { get; set; } = string.Empty;
    public string ScheduledTime { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsDone { get; set; } = false;
}

/// <summary>
/// Comprehensive confirmation & notification details DTO (UC22).
/// Consumed by Web Confirmation Page & Mobile Booking Result Screen.
/// </summary>
public class AppointmentConfirmationDto
{
    public string AppointmentId { get; set; } = string.Empty;
    public string AppointmentCode { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientEmail { get; set; } = string.Empty;
    public string? PatientPhone { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public DateTime AppointmentDate { get; set; }
    public string DateFormatted { get; set; } = string.Empty;
    public string TimeFormatted { get; set; } = string.Empty;
    public string Location { get; set; } = "Lab Room 02 · HQ";
    public string Status { get; set; } = "Confirmed";
    public string PaymentStatus { get; set; } = "Paid · VNPay";
    public string FastingInstructions { get; set; } = "Do not eat or drink anything except water for 12 hours before your collection.";
    public string QrCodeData { get; set; } = string.Empty;
    public NotificationDeliveryStatusDto DeliveryStatus { get; set; } = new();
    public List<ReminderTimelineItemDto> ReminderSchedule { get; set; } = new();
}
