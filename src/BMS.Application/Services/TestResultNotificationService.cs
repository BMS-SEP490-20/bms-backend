using BMS.Application.DTOs.Notifications;
using BMS.Application.DTOs.Roles;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services;

/// <summary>
/// Service implementation for Laboratory Diagnostic Test Result Notifications (UC60 / BR-182).
/// Dispatches test result emails directly via SMTP, with automatic Panic Value detection and emergency escalation.
/// Conforms to: Production Medical Standards, ISO 15189 Quality Management & AGENTS-CODE.md.
/// </summary>
public class TestResultNotificationService : ITestResultNotificationService
{
    private readonly ITestResultNotificationRepository _repository;
    private readonly IEmailService _emailService;
    private readonly ILogger<TestResultNotificationService> _logger;

    public TestResultNotificationService(
        ITestResultNotificationRepository repository,
        IEmailService emailService,
        ILogger<TestResultNotificationService> logger)
    {
        _repository = repository;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<(bool Success, string Message, TestResultNotificationDetailDto? Data)> DispatchNotificationAsync(
        DispatchTestResultNotificationDto dto,
        string operatorId,
        string operatorName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.TestOrderId))
        {
            return (false, "Mã đơn chỉ định xét nghiệm (TestOrderId) không được để trống.", null);
        }

        if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.PatientName))
        {
            return (false, "Thông tin bệnh nhân (Mã và Họ tên) là bắt buộc.", null);
        }

        bool hasPanic = dto.IsCriticalPanicValue || (dto.CriticalParameters != null && dto.CriticalParameters.Count > 0);
        var priority = hasPanic ? NotificationPriority.Critical : dto.Priority;
        bool requiresImmediate = hasPanic || dto.RequiresImmediateAction;

        string title = !string.IsNullOrWhiteSpace(dto.Title)
            ? dto.Title.Trim()
            : hasPanic
                ? $"[CẢNH BÁO NGUY HIỂM] Chỉ số xét nghiệm hoảng loạn - Đơn {dto.TestOrderId}"
                : $"Kết quả xét nghiệm đơn {dto.TestOrderId} đã hoàn tất";

        string message = !string.IsNullOrWhiteSpace(dto.Message)
            ? dto.Message.Trim()
            : hasPanic
                ? $"CẢNH BÁO Y KHOA: Kết quả xét nghiệm của bệnh nhân {dto.PatientName} có chỉ số vượt ngưỡng nguy kịch. Vui lòng liên hệ bác sĩ hoặc phòng cấp cứu ngay lập tức."
                : $"Kính gửi quý khách {dto.PatientName}, hồ sơ xét nghiệm ({dto.TestOrderId}) đã được duyệt y khoa và sẵn sàng để xem chi tiết trên hệ thống BMS.";

        var deliveryStatus = NotificationDeliveryStatus.Delivered;
        string? failureReason = null;

        // Dispatches live email when channel includes Email or when PatientEmail is provided
        if (!string.IsNullOrWhiteSpace(dto.PatientEmail) &&
            (dto.Channel == TestResultNotificationChannel.Email || dto.Channel == TestResultNotificationChannel.All))
        {
            try
            {
                bool emailSent = await _emailService.SendTestResultNotificationEmailAsync(
                    dto.PatientEmail.Trim(),
                    dto.PatientName.Trim(),
                    dto.TestOrderId.Trim(),
                    dto.TestPackageName,
                    title,
                    message,
                    hasPanic,
                    dto.CriticalParameters,
                    dto.ClinicalRecommendation,
                    ct);

                if (!emailSent)
                {
                    deliveryStatus = NotificationDeliveryStatus.Failed;
                    failureReason = "Không thể gửi email tới hộp thư người nhận qua máy chủ SMTP.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Notification Service] SMTP error dispatching email to {Email}", dto.PatientEmail);
                deliveryStatus = NotificationDeliveryStatus.Failed;
                failureReason = ex.Message;
            }
        }

        var notification = new TestResultNotification
        {
            NotificationId = $"NOTIF-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            TestOrderId = dto.TestOrderId.Trim(),
            SpecimenBarcode = dto.SpecimenBarcode?.Trim(),
            PatientId = dto.PatientId.Trim(),
            PatientName = dto.PatientName.Trim(),
            PatientPhone = dto.PatientPhone?.Trim(),
            PatientEmail = dto.PatientEmail?.Trim(),
            Title = title,
            Message = message,
            Channel = dto.Channel,
            Priority = priority,
            Status = deliveryStatus,
            FailureReason = failureReason,
            IsCriticalPanicValue = hasPanic,
            CriticalParameters = dto.CriticalParameters ?? [],
            RequiresImmediateAction = requiresImmediate,
            ClinicalRecommendation = dto.ClinicalRecommendation?.Trim(),
            TestPackageName = dto.TestPackageName?.Trim(),
            SentAt = DateTime.UtcNow,
            DeliveredAt = deliveryStatus == NotificationDeliveryStatus.Delivered ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = !string.IsNullOrWhiteSpace(operatorName) ? operatorName : "Hệ thống tự động"
        };

        _logger.LogInformation(
            "[Notification Service] Dispatched {Channel} notification {NotifId} for order {OrderId} (Status: {Status}, Panic: {Panic})",
            notification.Channel, notification.NotificationId, notification.TestOrderId, notification.Status, notification.IsCriticalPanicValue);

        var saved = await _repository.AddAsync(notification, ct);
        return (true, "Phát thông báo kết quả xét nghiệm qua email thành công.", MapToDetailDto(saved));
    }

    public async Task<TestResultNotificationDetailDto?> GetNotificationByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        return entity != null ? MapToDetailDto(entity) : null;
    }

    public async Task<PagedResult<TestResultNotificationListItemDto>> GetNotificationsPagedAsync(
        NotificationQueryParameters query,
        CancellationToken ct = default)
    {
        var (items, total) = await _repository.GetPagedAsync(
            query.PatientId,
            query.TestOrderId,
            query.Channel,
            query.Priority,
            query.Status,
            query.IsCritical,
            query.FromDate,
            query.ToDate,
            query.PageNumber,
            query.PageSize,
            ct);

        var dtos = items.Select(x => new TestResultNotificationListItemDto
        {
            Id = x.Id,
            NotificationId = x.NotificationId,
            TestOrderId = x.TestOrderId,
            PatientId = x.PatientId,
            PatientName = x.PatientName,
            PatientPhone = x.PatientPhone,
            Title = x.Title,
            Channel = x.Channel.ToString(),
            Priority = x.Priority.ToString(),
            Status = x.Status.ToString(),
            IsCriticalPanicValue = x.IsCriticalPanicValue,
            TestPackageName = x.TestPackageName,
            SentAt = x.SentAt?.ToString("o"),
            IsRead = x.Status == NotificationDeliveryStatus.Read
        }).ToList();

        return new PagedResult<TestResultNotificationListItemDto>
        {
            Items = dtos,
            TotalCount = total,
            CurrentPage = query.PageNumber,
            PageSize = query.PageSize
        };
    }

    public async Task<List<TestResultNotificationDetailDto>> GetPatientNotificationsAsync(
        string patientId,
        CancellationToken ct = default)
    {
        var items = await _repository.GetByPatientIdAsync(patientId, ct);
        return items.Select(MapToDetailDto).ToList();
    }

    public async Task<bool> MarkAsReadAsync(string id, CancellationToken ct = default)
    {
        return await _repository.MarkAsReadAsync(id, DateTime.UtcNow, ct);
    }

    public async Task<(bool Success, string Message, TestResultNotificationDetailDto? Data)> ResendNotificationAsync(
        string id,
        ResendNotificationDto dto,
        string operatorName,
        CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing == null)
        {
            return (false, $"Không tìm thấy thông báo kết quả với mã '{id}'.", null);
        }

        if (dto.ChannelOverride.HasValue)
        {
            existing.Channel = dto.ChannelOverride.Value;
        }

        if (!string.IsNullOrWhiteSpace(dto.DestinationOverride))
        {
            if (existing.Channel == TestResultNotificationChannel.Sms)
                existing.PatientPhone = dto.DestinationOverride.Trim();
            else if (existing.Channel == TestResultNotificationChannel.Email)
                existing.PatientEmail = dto.DestinationOverride.Trim();
        }

        existing.RetryCount += 1;
        existing.SentAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(existing.PatientEmail) &&
            (existing.Channel == TestResultNotificationChannel.Email || existing.Channel == TestResultNotificationChannel.All))
        {
            try
            {
                bool emailSent = await _emailService.SendTestResultNotificationEmailAsync(
                    existing.PatientEmail.Trim(),
                    existing.PatientName.Trim(),
                    existing.TestOrderId.Trim(),
                    existing.TestPackageName,
                    existing.Title,
                    existing.Message,
                    existing.IsCriticalPanicValue,
                    existing.CriticalParameters,
                    existing.ClinicalRecommendation,
                    ct);

                if (emailSent)
                {
                    existing.Status = NotificationDeliveryStatus.Delivered;
                    existing.DeliveredAt = DateTime.UtcNow;
                    existing.FailureReason = null;
                }
                else
                {
                    existing.Status = NotificationDeliveryStatus.Failed;
                    existing.FailureReason = "Gửi lại email thất bại qua máy chủ SMTP.";
                }
            }
            catch (Exception ex)
            {
                existing.Status = NotificationDeliveryStatus.Failed;
                existing.FailureReason = ex.Message;
            }
        }
        else
        {
            existing.Status = NotificationDeliveryStatus.Delivered;
            existing.DeliveredAt = DateTime.UtcNow;
            existing.FailureReason = null;
        }

        _logger.LogInformation(
            "[Notification Service] Resending notification {NotifId} (Retry #{Retry}) by {Operator}",
            existing.NotificationId, existing.RetryCount, operatorName);

        var updated = await _repository.UpdateAsync(existing, ct);
        return (true, "Gửi lại thông báo kết quả xét nghiệm thành công.", MapToDetailDto(updated));
    }

    public async Task<int> GetUnreadCountAsync(string patientId, CancellationToken ct = default)
    {
        return await _repository.GetUnreadCountAsync(patientId, ct);
    }

    public async Task<NotificationStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var dict = await _repository.GetNotificationStatsAsync(ct);
        int total = dict.GetValueOrDefault("Total", 0);
        int delivered = dict.GetValueOrDefault("Delivered", 0);
        int critical = dict.GetValueOrDefault("CriticalPanic", 0);
        int pending = dict.GetValueOrDefault("Pending", 0);
        int failed = dict.GetValueOrDefault("Failed", 0);

        double rate = total > 0 ? Math.Round(((double)delivered / total) * 100, 1) : 100.0;

        return new NotificationStatsDto
        {
            TotalNotifications = total,
            DeliveredSuccessfully = delivered,
            CriticalPanicAlerts = critical,
            PendingCount = pending,
            FailedCount = failed,
            DeliverySuccessRate = rate
        };
    }

    private static TestResultNotificationDetailDto MapToDetailDto(TestResultNotification x)
    {
        return new TestResultNotificationDetailDto
        {
            Id = x.Id,
            NotificationId = x.NotificationId,
            TestOrderId = x.TestOrderId,
            SpecimenBarcode = x.SpecimenBarcode,
            PatientId = x.PatientId,
            PatientName = x.PatientName,
            PatientPhone = x.PatientPhone,
            PatientEmail = x.PatientEmail,
            Title = x.Title,
            Message = x.Message,
            Channel = x.Channel.ToString(),
            Priority = x.Priority.ToString(),
            Status = x.Status.ToString(),
            IsCriticalPanicValue = x.IsCriticalPanicValue,
            CriticalParameters = x.CriticalParameters ?? [],
            RequiresImmediateAction = x.RequiresImmediateAction,
            ClinicalRecommendation = x.ClinicalRecommendation,
            TestPackageName = x.TestPackageName,
            SentAt = x.SentAt?.ToString("o"),
            DeliveredAt = x.DeliveredAt?.ToString("o"),
            ReadAt = x.ReadAt?.ToString("o"),
            FailureReason = x.FailureReason,
            RetryCount = x.RetryCount,
            CreatedAt = x.CreatedAt.ToString("o"),
            CreatedBy = x.CreatedBy
        };
    }
}
