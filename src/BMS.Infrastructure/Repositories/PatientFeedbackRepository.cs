using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Testorder_service.Data;
using Testorder_service.Models.Entities;
using Testorder_service.Repositories.Interface;

namespace Testorder_service.Repositories.Implement
{
    /// <summary>
    /// EF Core MongoDB repository implementation for PatientFeedback entity.
    /// </summary>
    public class PatientFeedbackRepository : IPatientFeedbackRepository
    {
        private readonly TestOrderManagementDbContext _context;

        public PatientFeedbackRepository(TestOrderManagementDbContext context)
        {
            _context = context;
        }


        public async Task<IEnumerable<PatientFeedback>> GetAllAsync(int? rating, string? category, string? status, string? searchTerm)
        {
            var query = _context.PatientFeedbacks.AsQueryable();

            if (rating.HasValue && rating.Value > 0)
            {
                query = query.Where(f => f.Rating == rating.Value);
            }

            if (!string.IsNullOrWhiteSpace(category) && category != "ALL")
            {
                query = query.Where(f => f.ServiceCategory == category);
            }

            if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
            {
                query = query.Where(f => f.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(f => f.Title.ToLower().Contains(term) ||
                                         f.Comment.ToLower().Contains(term) ||
                                         f.PatientName.ToLower().Contains(term) ||
                                         (f.DoctorName != null && f.DoctorName.ToLower().Contains(term)));
            }

            return await query.OrderByDescending(f => f.CreatedAt).ToListAsync();
        }

        public async Task<IEnumerable<PatientFeedback>> GetByPatientIdAsync(string patientId)
        {
            return await _context.PatientFeedbacks
                .Where(f => f.PatientId == patientId)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();
        }

        public async Task<PatientFeedback?> GetByIdAsync(string id)
        {
            return await _context.PatientFeedbacks.FirstOrDefaultAsync(f => f.FeedbackId == id);
        }

        public async Task AddAsync(PatientFeedback entity)
        {
            await _context.PatientFeedbacks.AddAsync(entity);
        }

        public Task UpdateAsync(PatientFeedback entity)
        {
            _context.PatientFeedbacks.Update(entity);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(PatientFeedback entity)
        {
            _context.PatientFeedbacks.Remove(entity);
            return Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
