using Microsoft.EntityFrameworkCore;

using TestOrder.Repositories.Interface;
using Testorder_service.Data;
using Testorder_service.Models.Entities;

namespace TestOrder.Repositories.Implement
{
    public class TestResultRepository : ITestResultRepository
    {
        private readonly TestOrderManagementDbContext _context;

        public TestResultRepository(TestOrderManagementDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<TestResult>> GetAllAsync()
            => await _context.TestResults.ToListAsync();

        public async Task<TestResult?> GetByIdAsync(string id)
            => await _context.TestResults.FindAsync(id);

        public async Task AddAsync(TestResult entity)
        {
            await _context.TestResults.AddAsync(entity);
        }

        public async Task UpdateAsync(TestResult entity)
        {
            _context.TestResults.Update(entity);
            await Task.CompletedTask;
        }

        public async Task DeleteAsync(string id)
        {
            var existing = await GetByIdAsync(id);
            if (existing != null)
                _context.TestResults.Remove(existing);
        }

        public async Task SaveChangesAsync()
            => await _context.SaveChangesAsync();
        public async Task<IEnumerable<TestResult>> GetByAppointmentIdAsync(string appointmentId)
        {
            return await _context.TestResults
                .Where(x => x.AppointmentId == appointmentId)
                .ToListAsync();
        }

    }
}
