using System;
using System.ComponentModel.DataAnnotations;

namespace Testorder_service.Models.DTO
{
    /// <summary>
    /// Data transfer object for system notifications history display.
    /// </summary>
    public class NotificationDto
    {
        public string NotificationId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string? Role { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? ReferenceId { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }

    /// <summary>
    /// Payload for creating a new in-app system notification.
    /// </summary>
    public class CreateNotificationDto
    {
        [Required]
        public string UserId { get; set; } = string.Empty;
        public string? Role { get; set; }
        [Required]
        public string Title { get; set; } = string.Empty;
        [Required]
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = "system_alert";
        public string? ReferenceId { get; set; }
    }
}
