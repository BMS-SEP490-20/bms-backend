using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.AuditLogs;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces
{
    /// <summary>
    /// Service contract for recording and querying security, operational, and data modification audit trails.
    /// </summary>
    public interface IAuditLogService
    {
        /// <summary>
        /// Retrieves paginated and filtered audit logs.
        /// </summary>
        Task<(IReadOnlyList<AuditLog> Logs, long TotalCount)> GetAuditLogsAsync(AuditLogFilterDto filter, CancellationToken ct = default);

        /// <summary>
        /// Retrieves a single audit log entry by its identifier.
        /// </summary>
        Task<AuditLog?> GetByIdAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// Records an audit log event into the repository.
        /// </summary>
        Task<AuditLog> LogAsync(CreateAuditLogDto dto, CancellationToken ct = default);

        /// <summary>
        /// Computes operational audit statistics (total events, severity breakdown, top actions).
        /// </summary>
        Task<AuditLogStatistics> GetStatisticsAsync(CancellationToken ct = default);
    }
}
