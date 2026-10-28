using System.Collections.Generic;
using System.Threading.Tasks;
using Testorder_service.Models.Entities;

namespace Testorder_service.Repositories.Interface
{
    /// <summary>
    /// Repository interface for persisting and querying system notifications across all roles.
    /// </summary>
    public interface INotificationRepository
    {
        /// <summary>
        /// Retrieves all notifications matching recipient user ID or target broadcast roles.
        /// </summary>
        Task<IEnumerable<Notification>> GetByUserIdAsync(string userId);

        /// <summary>
        /// Retrieves notifications specifically addressed to a given user role.
        /// </summary>
        Task<IEnumerable<Notification>> GetByRoleAsync(string role);

        /// <summary>
        /// Retrieves notifications matching user ID or their assigned role.
        /// </summary>
        Task<IEnumerable<Notification>> GetByUserAndRoleAsync(string userId, string? role);

        /// <summary>
        /// Retrieves a single notification record by its unique identifier.
        /// </summary>
        Task<Notification?> GetByIdAsync(string id);

        /// <summary>
        /// Adds a new notification to the database context.
        /// </summary>
        Task AddAsync(Notification entity);

        /// <summary>
        /// Updates an existing notification record.
        /// </summary>
        Task UpdateAsync(Notification entity);

        /// <summary>
        /// Commits pending database operations.
        /// </summary>
        Task SaveChangesAsync();
    }
}
