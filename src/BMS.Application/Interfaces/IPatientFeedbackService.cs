using System.Collections.Generic;
using System.Threading.Tasks;
using Testorder_service.Models.DTO;
using Testorder_service.Models.Entities;

namespace Testorder_service.Services.Interface
{
    /// <summary>
    /// Service contract for handling patient feedback, ratings, and management responses.
    /// </summary>
    public interface IPatientFeedbackService
    {
        /// <summary>
        /// Retrieves all feedback records filtered by rating, category, status, or search term.
        /// </summary>
        Task<IEnumerable<PatientFeedback>> GetAllAsync(int? rating, string? category, string? status, string? searchTerm);

        /// <summary>
        /// Retrieves all feedback submitted by a specific patient.
        /// </summary>
        Task<IEnumerable<PatientFeedback>> GetByPatientIdAsync(string patientId);

        /// <summary>
        /// Retrieves a specific feedback record by its unique ID.
        /// </summary>
        Task<PatientFeedback?> GetByIdAsync(string id);

        /// <summary>
        /// Creates and persists a new patient feedback entry.
        /// </summary>
        Task<PatientFeedback> CreateAsync(CreatePatientFeedbackDto dto);

        /// <summary>
        /// Updates an existing feedback record.
        /// </summary>
        Task<PatientFeedback?> UpdateAsync(string id, UpdatePatientFeedbackDto dto);

        /// <summary>
        /// Submits a clinic manager response to a patient feedback.
        /// </summary>
        Task<PatientFeedback?> RespondAsync(string id, RespondFeedbackDto dto);

        /// <summary>
        /// Deletes a feedback record from the database.
        /// </summary>
        Task<bool> DeleteAsync(string id);

        /// <summary>
        /// Computes feedback statistics including total count, average rating, and response rates.
        /// </summary>
        Task<object> GetStatisticsAsync();
    }
}

