using System.Collections.Generic;
using System.Threading.Tasks;
using Testorder_service.Models.DTO;

namespace Testorder_service.Services.Interface
{
    /// <summary>
    /// Service contract for managing, querying, and updating notifications across Patient, Doctor, and Staff roles.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Retrieves list of notifications for a specific user ID, with fallback sample history if none exist.
        /// </summary>
        Task<IEnumerable<NotificationDto>> GetNotificationsForUserAsync(string userId, string? role);

        /// <summary>
        /// Retrieves notifications filtered by audience role (Patient, Doctor, Staff).
        /// </summary>
        Task<IEnumerable<NotificationDto>> GetNotificationsByRoleAsync(string role);

        /// <summary>
        /// Marks a single notification record as read.
        /// </summary>
        Task<bool> MarkAsReadAsync(string notificationId);

        /// <summary>
        /// Marks all notifications for a specific user as read.
        /// </summary>
        Task<bool> MarkAllAsReadAsync(string userId);

        /// <summary>
        /// Creates and dispatches a new in-app system notification.
        /// </summary>
        Task<NotificationDto> CreateNotificationAsync(CreateNotificationDto dto);
    }
}
