using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.QC;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces
{
    /// <summary>
    /// Service contract for managing Quality Control (QC) flags, rules, and resolutions.
    /// </summary>
    public interface IQcFlagService
    {
        /// <summary>
        /// Retrieves QC flags filtered by status, severity, analyzer ID, or test parameter.
        /// </summary>
        Task<IReadOnlyList<QcFlag>> GetAllAsync(string? status, string? severity, string? analyzerId, string? parameter, CancellationToken ct = default);

        /// <summary>
        /// Retrieves a single QC flag by its identifier.
        /// </summary>
        Task<QcFlag?> GetByIdAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// Creates a new QC flag triggered by an analyzer or custom quality threshold rule.
        /// </summary>
        Task<QcFlag> CreateAsync(CreateQcFlagDto dto, CancellationToken ct = default);

        /// <summary>
        /// Updates an existing QC flag (e.g., status changes, resolution notes, root cause).
        /// </summary>
        Task<QcFlag?> UpdateAsync(string id, UpdateQcFlagDto dto, CancellationToken ct = default);

        /// <summary>
        /// Deletes a QC flag record from the database.
        /// </summary>
        Task<bool> DeleteAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// Calculates QC flag metrics (total flags, unresolved count, severity counts).
        /// </summary>
        Task<QcFlagStatisticsDto> GetStatisticsAsync(CancellationToken ct = default);
    }
}

