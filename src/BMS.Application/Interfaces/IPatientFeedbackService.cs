using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.PatientFeedback;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces
{
    /// <summary>
    /// Service contract for handling patient feedback, ratings, and management responses.
    /// </summary>
    public interface IPatientFeedbackService
    {
        /// <summary>
        /// Retrieves all feedback records filtered by rating, category, status, or search term.
        /// </summary>
        Task<IReadOnlyList<PatientFeedback>> GetAllAsync(int? rating, string? category, string? status, string? searchTerm, CancellationToken ct = default);

        /// <summary>
        /// Retrieves all feedback submitted by a specific patient.
        /// </summary>
        Task<IReadOnlyList<PatientFeedback>> GetByPatientIdAsync(string patientId, CancellationToken ct = default);

        /// <summary>
        /// Retrieves a specific feedback record by its unique ID.
        /// </summary>
        Task<PatientFeedback?> GetByIdAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// Creates and persists a new patient feedback entry.
        /// </summary>
        Task<PatientFeedback> CreateAsync(CreatePatientFeedbackDto dto, CancellationToken ct = default);

        /// <summary>
        /// Updates an existing feedback record.
        /// </summary>
        Task<PatientFeedback?> UpdateAsync(string id, UpdatePatientFeedbackDto dto, CancellationToken ct = default);

        /// <summary>
        /// Submits a clinic manager response to a patient feedback.
        /// </summary>
        Task<PatientFeedback?> RespondAsync(string id, RespondFeedbackDto dto, CancellationToken ct = default);

        /// <summary>
        /// Deletes a feedback record from the database.
        /// </summary>
        Task<bool> DeleteAsync(string id, CancellationToken ct = default);

        /// <summary>
        /// Computes feedback statistics including total count, average rating, and response rates.
        /// </summary>
        Task<PatientFeedbackStatistics> GetStatisticsAsync(CancellationToken ct = default);
    }
}

