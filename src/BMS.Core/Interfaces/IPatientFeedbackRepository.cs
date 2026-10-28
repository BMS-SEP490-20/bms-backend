using System.Collections.Generic;
using System.Threading.Tasks;
using Testorder_service.Models.Entities;

namespace Testorder_service.Repositories.Interface
{
    /// <summary>
    /// Repository interface for PatientFeedback persistence operations in MongoDB.
    /// </summary>
    public interface IPatientFeedbackRepository
    {
        /// <summary>
        /// Retrieves filtered patient feedback entries from MongoDB.
        /// </summary>
        Task<IEnumerable<PatientFeedback>> GetAllAsync(int? rating, string? category, string? status, string? searchTerm);

        /// <summary>
        /// Retrieves feedback entries submitted by a specific patient ID.
        /// </summary>
        Task<IEnumerable<PatientFeedback>> GetByPatientIdAsync(string patientId);

        /// <summary>
        /// Retrieves a feedback entity by its identifier.
        /// </summary>
        Task<PatientFeedback?> GetByIdAsync(string id);

        /// <summary>
        /// Adds a new feedback entity to the database context.
        /// </summary>
        Task AddAsync(PatientFeedback entity);

        /// <summary>
        /// Marks a feedback entity as modified in the database context.
        /// </summary>
        Task UpdateAsync(PatientFeedback entity);

        /// <summary>
        /// Removes a feedback entity from the database context.
        /// </summary>
        Task DeleteAsync(PatientFeedback entity);

        /// <summary>
        /// Asynchronously commits all pending changes to MongoDB.
        /// </summary>
        Task SaveChangesAsync();
    }
}

