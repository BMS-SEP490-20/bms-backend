using System.Collections.Generic;
using System.Threading.Tasks;
using Testorder_service.Models.DTO;
using Testorder_service.Models.Entities;

namespace Testorder_service.Repositories.Interface
{
    /// <summary>
    /// Repository interface for querying and saving AuditLog records in MongoDB.
    /// </summary>
    public interface IAuditLogRepository
    {
        /// <summary>
        /// Retrieves filtered audit logs with pagination from MongoDB.
        /// </summary>
        Task<IEnumerable<AuditLog>> GetAllAsync(AuditLogFilterDto filter);

        /// <summary>
        /// Gets total matching count of audit logs for pagination calculation.
        /// </summary>
        Task<int> GetCountAsync(AuditLogFilterDto filter);

        /// <summary>
        /// Retrieves an audit log by its unique record ID.
        /// </summary>
        Task<AuditLog?> GetByIdAsync(string id);

        /// <summary>
        /// Inserts a new audit log record into the database context.
        /// </summary>
        Task AddAsync(AuditLog entity);

        /// <summary>
        /// Asynchronously commits pending audit log changes to MongoDB.
        /// </summary>
        Task SaveChangesAsync();
    }
}

