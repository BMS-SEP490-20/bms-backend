using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Notifications;

/// <summary>
/// Request DTO for dispatching a push notification (UC58 / Send Push Notifications).
/// Conforms to: Production Engineering Standards & AGENTS-DESIGN.md.
/// </summary>
public class DispatchNotificationRequestDto
{
    [Required]
    public string RecipientUserId { get; set; } = string.Empty;

    public string? RecipientDeviceToken { get; set; }

    [Required]
    [StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(2000, MinimumLength = 3)]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// PUSH_FCM, IN_APP_WEB, SMS, EMAIL, ALL.
    /// </summary>
    public string Channel { get; set; } = "ALL";

    /// <summary>
    /// NORMAL, HIGH, CRITICAL_PANIC.
    /// </summary>
    public string Priority { get; set; } = "NORMAL";

    public string? ActionUrl { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Response DTO after dispatching a notification.
/// </summary>
public class DispatchNotificationResultDto
{
    public bool Success { get; set; }
    public string NotificationId { get; set; } = string.Empty;
    public string RecipientUserId { get; set; } = string.Empty;
    public string Status { get; set; } = "DELIVERED"; // PENDING, DELIVERED, FAILED
    public string Channel { get; set; } = "ALL";
    public string Priority { get; set; } = "NORMAL";
    public DateTime DispatchedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }
    public int RetryCount { get; set; } = 0;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Request DTO for registering an FCM or Web Push device token.
/// </summary>
public class RegisterDeviceTokenRequestDto
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string DeviceToken { get; set; } = string.Empty;

    public string Platform { get; set; } = "Android"; // Android, iOS, Web
}

/// <summary>
/// Query filters for viewing push notification audit logs.
/// </summary>
public class PushNotificationQueryParameters
{
    public string? UserId { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public string? Channel { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
