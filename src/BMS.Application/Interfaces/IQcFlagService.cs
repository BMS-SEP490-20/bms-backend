using System.Collections.Generic;
using System.Threading.Tasks;
using Testorder_service.Models.DTO;
using Testorder_service.Models.Entities;

namespace Testorder_service.Services.Interface
{
    /// <summary>
    /// Service contract for managing Quality Control (QC) flags, rules, and resolutions.
    /// </summary>
    public interface IQcFlagService
    {
        /// <summary>
        /// Retrieves QC flags filtered by status, severity, analyzer ID, or test parameter.
        /// </summary>
        Task<IEnumerable<QcFlag>> GetAllAsync(string? status, string? severity, string? analyzerId, string? parameter);

        /// <summary>
        /// Retrieves a single QC flag by its identifier.
        /// </summary>
        Task<QcFlag?> GetByIdAsync(string id);

        /// <summary>
        /// Creates a new QC flag triggered by an analyzer or custom quality threshold rule.
        /// </summary>
        Task<QcFlag> CreateAsync(CreateQcFlagDto dto);

        /// <summary>
        /// Updates an existing QC flag (e.g., status changes, resolution notes, root cause).
        /// </summary>
        Task<QcFlag?> UpdateAsync(string id, UpdateQcFlagDto dto);

        /// <summary>
        /// Deletes a QC flag record from the database.
        /// </summary>
        Task<bool> DeleteAsync(string id);

        /// <summary>
        /// Calculates QC flag metrics (total flags, unresolved count, severity counts).
        /// </summary>
        Task<object> GetStatisticsAsync();
    }
}

