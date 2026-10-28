using System.Collections.Generic;
using System.Threading.Tasks;
using Testorder_service.Models.Entities;

namespace Testorder_service.Repositories.Interface
{
    /// <summary>
    /// Repository interface for QC Flag data operations in MongoDB.
    /// </summary>
    public interface IQcFlagRepository
    {
        /// <summary>
        /// Retrieves filtered QC flags from MongoDB.
        /// </summary>
        Task<IEnumerable<QcFlag>> GetAllAsync(string? status, string? severity, string? analyzerId, string? parameter);

        /// <summary>
        /// Retrieves a QC flag by its unique flag ID.
        /// </summary>
        Task<QcFlag?> GetByIdAsync(string id);

        /// <summary>
        /// Adds a new QC flag entity to the context.
        /// </summary>
        Task AddAsync(QcFlag entity);

        /// <summary>
        /// Marks a QC flag entity as updated.
        /// </summary>
        Task UpdateAsync(QcFlag entity);

        /// <summary>
        /// Removes a QC flag entity from the context.
        /// </summary>
        Task DeleteAsync(QcFlag entity);

        /// <summary>
        /// Commits pending QC flag changes to MongoDB.
        /// </summary>
        Task SaveChangesAsync();
    }
}

