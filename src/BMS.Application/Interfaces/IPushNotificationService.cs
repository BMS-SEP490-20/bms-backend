using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.Notifications;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for dispatching multi-channel push notifications and managing audit logs (UC58 / Send Push Notifications).
/// Conforms to: Production Engineering Standards & AGENTS-DESIGN.md.
/// </summary>
public interface IPushNotificationService
{
    /// <summary>
    /// Dispatches a push notification across FCM, Web, and SMS/Email with rate limiting and retry handling.
    /// </summary>
    Task<DispatchNotificationResultDto> DispatchNotificationAsync(DispatchNotificationRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers or updates an FCM / Web Push device token for a specific user.
    /// </summary>
    Task<bool> RegisterDeviceTokenAsync(RegisterDeviceTokenRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves user notifications with pagination.
    /// </summary>
    Task<PagedResult<PushNotificationLog>> GetUserNotificationsAsync(string userId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a specific notification as READ.
    /// </summary>
    Task<bool> MarkAsReadAsync(string notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks all unread notifications for a user as READ.
    /// </summary>
    Task<int> MarkAllAsReadAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves notification audit logs with filters (Status, Channel, Priority, Date Range).
    /// </summary>
    Task<PagedResult<PushNotificationLog>> GetAuditLogsAsync(PushNotificationQueryParameters query, CancellationToken cancellationToken = default);
}
