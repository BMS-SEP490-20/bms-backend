using System;
using System.ComponentModel.DataAnnotations;

namespace Testorder_service.Models.Entities
{
    /// <summary>
    /// Represents an in-system notification record dispatched to patients, doctors, or staff members.
    /// Captures appointment reminders, test result readiness, receipt generation, and system alerts.
    /// </summary>
    public class Notification
    {
        /// <summary>
        /// Unique primary identifier for the notification record.
        /// </summary>
        [Key]
        public string NotificationId { get; set; } = null!;

        /// <summary>
        /// Recipient user identifier. Can be a specific user or 'ALL' for broadcast.
        /// </summary>
        public string UserId { get; set; } = null!;

        /// <summary>
        /// Target role audience (e.g., Patient, Doctor, Staff, Receptionist, Manager, All).
        /// </summary>
        public string? Role { get; set; }

        /// <summary>
        /// Title header of the system notification.
        /// </summary>
        public string Title { get; set; } = null!;

        /// <summary>
        /// Detailed message body explaining the event or reminder.
        /// </summary>
        public string Message { get; set; } = null!;

        /// <summary>
        /// Notification category: appointment_reminder, result_ready, system_alert, receipt_issued.
        /// </summary>
        public string Type { get; set; } = "system_alert";

        /// <summary>
        /// Associated domain entity identifier (e.g., AppointmentId, TestResultId, ReceiptNumber).
        /// </summary>
        public string? ReferenceId { get; set; }

        /// <summary>
        /// Status flag indicating whether the notification has been viewed by the recipient.
        /// </summary>
        public bool IsRead { get; set; } = false;

        /// <summary>
        /// Timestamp when the notification was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when the recipient marked this notification as read.
        /// </summary>
        public DateTime? ReadAt { get; set; }
    }
}
