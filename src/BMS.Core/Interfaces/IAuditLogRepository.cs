using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces
{
    /// <summary>
    /// Repository interface for querying and saving AuditLog records in MongoDB.
    /// </summary>
    public interface IAuditLogRepository
    {
        /// <summary>
        /// Retrieves filtered audit logs with pagination from MongoDB.
        /// </summary>
        Task<IReadOnlyList<AuditLog>> GetAllAsync(AuditLogQuery filter, CancellationToken ct = default);

        /// <summary>
        /// Gets total matching count of audit logs for pagination calculation.
        /// </summary>
        Task<long> GetCountAsync(AuditLogQuery filter, CancellationToken ct = default);

        /// <summary>
        /// Retrieves an audit log by its unique record ID.
        /// </summary>
        Task<AuditLog?> GetByIdAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// Inserts a new audit log record. MongoDB writes are immediate, so there is no separate SaveChanges step.
        /// </summary>
        Task AddAsync(AuditLog entity, CancellationToken ct = default);

        /// <summary>
        /// Computes audit counters (total, security, access, critical, failed) with server-side counts
        /// instead of loading documents into memory.
        /// </summary>
        Task<AuditLogStatistics> GetStatisticsAsync(CancellationToken ct = default);
    }
}
