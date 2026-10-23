using System.ComponentModel.DataAnnotations;
using BMS.Core.Entities;

namespace BMS.Application.DTOs.Notifications;

/// <summary>
/// Query filter parameters for retrieving notifications list (UC60 / BR-182).
/// </summary>
public class NotificationQueryParameters
{
    public string? PatientId { get; set; }
    public string? TestOrderId { get; set; }
    public TestResultNotificationChannel? Channel { get; set; }
    public NotificationPriority? Priority { get; set; }
    public NotificationDeliveryStatus? Status { get; set; }
    public bool? IsCritical { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 15;
}

/// <summary>
/// Request payload to dispatch a new test result notification (UC60).
/// </summary>
public class DispatchTestResultNotificationDto
{
    [Required(ErrorMessage = "Mã đơn chỉ định xét nghiệm (TestOrderId) là bắt buộc.")]
    public string TestOrderId { get; set; } = string.Empty;

    public string? SpecimenBarcode { get; set; }

    [Required(ErrorMessage = "Mã bệnh nhân (PatientId) là bắt buộc.")]
    public string PatientId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ tên bệnh nhân (PatientName) là bắt buộc.")]
    public string PatientName { get; set; } = string.Empty;

    public string? PatientPhone { get; set; }

    public string? PatientEmail { get; set; }

    public string? Title { get; set; }

    public string? Message { get; set; }

    public TestResultNotificationChannel Channel { get; set; } = TestResultNotificationChannel.Email;

    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

    public bool IsCriticalPanicValue { get; set; }

    public List<string> CriticalParameters { get; set; } = [];

    public bool RequiresImmediateAction { get; set; }

    public string? ClinicalRecommendation { get; set; }

    public string? TestPackageName { get; set; }
}

/// <summary>
/// Detailed DTO for test result notification view and modal (UC60).
/// </summary>
public class TestResultNotificationDetailDto
{
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
    public string Channel { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsCriticalPanicValue { get; set; }
    public List<string> CriticalParameters { get; set; } = [];
    public bool RequiresImmediateAction { get; set; }
    public string? ClinicalRecommendation { get; set; }
    public string? TestPackageName { get; set; }
    public string? SentAt { get; set; }
    public string? DeliveredAt { get; set; }
    public string? ReadAt { get; set; }
    public string? FailureReason { get; set; }
    public int RetryCount { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Row summary DTO for notification list display (UC60).
/// </summary>
public class TestResultNotificationListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string NotificationId { get; set; } = string.Empty;
    public string TestOrderId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? PatientPhone { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsCriticalPanicValue { get; set; }
    public string? TestPackageName { get; set; }
    public string? SentAt { get; set; }
    public bool IsRead { get; set; }
}

/// <summary>
/// Request to resend an existing notification.
/// </summary>
public class ResendNotificationDto
{
    public TestResultNotificationChannel? ChannelOverride { get; set; }
    public string? DestinationOverride { get; set; }
}

/// <summary>
/// Operational statistics for test result notification management dashboard.
/// </summary>
public class NotificationStatsDto
{
    public int TotalNotifications { get; set; }
    public int DeliveredSuccessfully { get; set; }
    public int CriticalPanicAlerts { get; set; }
    public int PendingCount { get; set; }
    public int FailedCount { get; set; }
    public double DeliverySuccessRate { get; set; }
}

/// <summary>
/// DTO representing a patient with completed laboratory test results ready for notification dispatch (UC60).
/// </summary>
public class CompletedTestPatientDto
{
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? PatientPhone { get; set; }
    public string? PatientEmail { get; set; }
    public string TestOrderId { get; set; } = string.Empty;
    public string? SpecimenBarcode { get; set; }
    public string? TestPackageName { get; set; }
    public string? ConclusionNotes { get; set; }
    public bool IsCritical { get; set; }
    public DateTime? CompletedAt { get; set; }
}
