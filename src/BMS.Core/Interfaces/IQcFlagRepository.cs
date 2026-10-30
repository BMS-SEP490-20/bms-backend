using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces
{
    /// <summary>
    /// Repository interface for QC Flag data operations in MongoDB.
    /// </summary>
    public interface IQcFlagRepository
    {
        /// <summary>
        /// Retrieves filtered QC flags from MongoDB (newest first, bounded).
        /// </summary>
        Task<IReadOnlyList<QcFlag>> GetAllAsync(string? status, string? severity, string? analyzerId, string? parameter, CancellationToken ct = default);

        /// <summary>
        /// Retrieves a QC flag by its unique flag ID.
        /// </summary>
        Task<QcFlag?> GetByIdAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// Inserts a new QC flag (MongoDB writes are immediate; no SaveChanges step).
        /// </summary>
        Task AddAsync(QcFlag entity, CancellationToken ct = default);

        /// <summary>
        /// Replaces an existing QC flag document. Returns false when it no longer exists.
        /// </summary>
        Task<bool> UpdateAsync(QcFlag entity, CancellationToken ct = default);

        /// <summary>
        /// Removes a QC flag. Returns false when it does not exist.
        /// </summary>
        Task<bool> DeleteAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// Counts flags matching an optional status and/or severity on the server.
        /// </summary>
        Task<long> CountAsync(string? status = null, string? severity = null, CancellationToken ct = default);
    }
}
