using System.Collections.Generic;
using System.Threading.Tasks;
using Testorder_service.Models.DTO;
using Testorder_service.Models.Entities;

namespace Testorder_service.Services.Interface
{
    /// <summary>
    /// Service contract for recording and querying security, operational, and data modification audit trails.
    /// </summary>
    public interface IAuditLogService
    {
        /// <summary>
        /// Retrieves paginated and filtered audit logs.
        /// </summary>
        Task<(IEnumerable<AuditLog> Logs, int TotalCount)> GetAuditLogsAsync(AuditLogFilterDto filter);

        /// <summary>
        /// Retrieves a single audit log entry by its identifier.
        /// </summary>
        Task<AuditLog?> GetByIdAsync(string id);

        /// <summary>
        /// Records an audit log event into the repository.
        /// </summary>
        Task<AuditLog> LogAsync(CreateAuditLogDto dto);

        /// <summary>
        /// Computes operational audit statistics (total events, severity breakdown, top actions).
        /// </summary>
        Task<object> GetStatisticsAsync();
    }
}

