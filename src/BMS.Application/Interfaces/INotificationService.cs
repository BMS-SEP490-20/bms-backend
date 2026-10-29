using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Notifications;

namespace BMS.Application.Interfaces
{
    /// <summary>
    /// Service contract for managing, querying, and updating notifications across Patient, Doctor, and Staff roles.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Retrieves list of notifications for a specific user ID, newest first.
        /// </summary>
        Task<IReadOnlyList<NotificationDto>> GetNotificationsForUserAsync(string userId, string? role, CancellationToken ct = default);

        /// <summary>
        /// Retrieves notifications filtered by audience role (Patient, Doctor, Staff).
        /// </summary>
        Task<IReadOnlyList<NotificationDto>> GetNotificationsByRoleAsync(string role, CancellationToken ct = default);

        /// <summary>
        /// Marks a single notification record as read.
        /// </summary>
        Task<bool> MarkAsReadAsync(string notificationId, CancellationToken ct = default);

        /// <summary>
        /// Marks all notifications for a specific user as read.
        /// </summary>
        Task<bool> MarkAllAsReadAsync(string userId, CancellationToken ct = default);

        /// <summary>
        /// Creates and dispatches a new in-app system notification.
        /// </summary>
        Task<NotificationDto> CreateNotificationAsync(CreateNotificationDto dto, CancellationToken ct = default);
    }
}
