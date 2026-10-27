using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Testorder_service.Data;
using Testorder_service.Models.Entities;
using Testorder_service.Repositories.Interface;

namespace Testorder_service.Repositories.Implement
{
    public class QcFlagRepository : IQcFlagRepository
    {
        private readonly TestOrderManagementDbContext _context;

        public QcFlagRepository(TestOrderManagementDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<QcFlag>> GetAllAsync(string? status, string? severity, string? analyzerId, string? parameter)
        {
            var query = _context.QcFlags.AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
            {
                query = query.Where(q => q.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(severity) && severity != "ALL")
            {
                query = query.Where(q => q.Severity == severity);
            }

            if (!string.IsNullOrWhiteSpace(analyzerId) && analyzerId != "ALL")
            {
                query = query.Where(q => q.AnalyzerId == analyzerId);
            }

            if (!string.IsNullOrWhiteSpace(parameter) && parameter != "ALL")
            {
                var param = parameter.Trim().ToLower();
                query = query.Where(q => q.Parameter.ToLower().Contains(param));
            }

            return await query.OrderByDescending(q => q.TriggeredAt).ToListAsync();
        }

        public async Task<QcFlag?> GetByIdAsync(string id)
        {
            return await _context.QcFlags.FirstOrDefaultAsync(q => q.FlagId == id);
        }

        public async Task AddAsync(QcFlag entity)
        {
            await _context.QcFlags.AddAsync(entity);
        }

        public Task UpdateAsync(QcFlag entity)
        {
            _context.QcFlags.Update(entity);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(QcFlag entity)
        {
            _context.QcFlags.Remove(entity);
            return Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
