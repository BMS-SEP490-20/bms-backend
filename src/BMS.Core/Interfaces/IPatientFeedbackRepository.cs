using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces
{
    /// <summary>
    /// Repository interface for PatientFeedback persistence operations in MongoDB.
    /// </summary>
    public interface IPatientFeedbackRepository
    {
        /// <summary>
        /// Retrieves filtered patient feedback entries from MongoDB (newest first, bounded).
        /// </summary>
        Task<IReadOnlyList<PatientFeedback>> GetAllAsync(int? rating, string? category, string? status, string? searchTerm, CancellationToken ct = default);

        /// <summary>
        /// Retrieves feedback entries submitted by a specific patient ID.
        /// </summary>
        Task<IReadOnlyList<PatientFeedback>> GetByPatientIdAsync(string patientId, CancellationToken ct = default);

        /// <summary>
        /// Retrieves a feedback entity by its identifier.
        /// </summary>
        Task<PatientFeedback?> GetByIdAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// Inserts a new feedback entry (MongoDB writes are immediate; no SaveChanges step).
        /// </summary>
        Task AddAsync(PatientFeedback entity, CancellationToken ct = default);

        /// <summary>
        /// Replaces a feedback document. Returns false when it no longer exists.
        /// </summary>
        Task<bool> UpdateAsync(PatientFeedback entity, CancellationToken ct = default);

        /// <summary>
        /// Computes feedback statistics with a server-side aggregation (DELETED entries excluded).
        /// </summary>
        Task<PatientFeedbackStatistics> GetStatisticsAsync(CancellationToken ct = default);
    }
}
