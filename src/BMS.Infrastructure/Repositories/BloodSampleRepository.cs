using Microsoft.EntityFrameworkCore;

using TestOrder.Repositories.Interface;
using Testorder_service.Data;
using Testorder_service.Models.Entities;

namespace TestOrder.Repositories.Implement
{
    public class BloodSampleRepository : IBloodSampleRepository
    {
        private readonly TestOrderManagementDbContext _context;

        public BloodSampleRepository(TestOrderManagementDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<BloodSample>> GetAllAsync()
        {
            return await _context.BloodSamples
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<BloodSample?> GetByIdAsync(string id)
        {
            return await _context.BloodSamples
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SampleId == id);
        }

        public async Task<BloodSample> CreateAsync(BloodSample entity)
        {
            _context.BloodSamples.Add(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public async Task UpdateAsync(BloodSample entity)
        {
            _context.BloodSamples.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(BloodSample entity)
        {
            _context.BloodSamples.Remove(entity);
            await _context.SaveChangesAsync();
        }
        public async Task<IEnumerable<BloodSample>> GetByAppointmentIdAsync(string appointmentId)
        {
            return await _context.BloodSamples
                .AsNoTracking()
                .Where(x => x.AppointmentId == appointmentId)
                .ToListAsync();
        }

    }
}
