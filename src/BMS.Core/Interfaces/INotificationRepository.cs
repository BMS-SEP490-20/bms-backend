using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces
{
    /// <summary>
    /// Repository interface for persisting and querying system notifications across all roles.
    /// Results are newest first and capped (see <see cref="MaxResults"/>) to keep responses bounded.
    /// </summary>
    public interface INotificationRepository
    {
        /// <summary>Maximum number of notifications returned by list queries.</summary>
        const int MaxResults = 200;

        /// <summary>
        /// Retrieves all notifications matching recipient user ID or target broadcast roles.
        /// </summary>
        Task<IReadOnlyList<Notification>> GetByUserIdAsync(string userId, CancellationToken ct = default);

        /// <summary>
        /// Retrieves notifications specifically addressed to a given user role.
        /// </summary>
        Task<IReadOnlyList<Notification>> GetByRoleAsync(string role, CancellationToken ct = default);

        /// <summary>
        /// Retrieves notifications matching user ID or their assigned role.
        /// </summary>
        Task<IReadOnlyList<Notification>> GetByUserAndRoleAsync(string userId, string? role, CancellationToken ct = default);

        /// <summary>
        /// Retrieves a single notification record by its unique identifier.
        /// </summary>
        Task<Notification?> GetByIdAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// Inserts a new notification (MongoDB writes are immediate; no SaveChanges step).
        /// </summary>
        Task AddAsync(Notification entity, CancellationToken ct = default);

        /// <summary>
        /// Atomically marks one notification as read. Returns false when it does not exist.
        /// </summary>
        Task<bool> MarkAsReadAsync(string id, DateTime readAt, CancellationToken ct = default);

        /// <summary>
        /// Marks every unread notification addressed directly to the user as read with one UpdateMany.
        /// Returns the number of modified notifications.
        /// </summary>
        Task<long> MarkAllAsReadAsync(string userId, DateTime readAt, CancellationToken ct = default);
    }
}
