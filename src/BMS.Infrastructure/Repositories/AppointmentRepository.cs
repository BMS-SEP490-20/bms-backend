using Microsoft.EntityFrameworkCore;
using TestOrder.Repositories.Interface;
using Testorder_service.Data;
using Testorder_service.Models.Entities;

namespace TestOrder.Repositories.Implement
{
    public class AppointmentRepository : IAppointmentRepository
    {
        private readonly TestOrderManagementDbContext _context;

        public AppointmentRepository(TestOrderManagementDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Appointment>> GetAllAsync()
        {
            return await _context.Appointments
                
                .OrderByDescending(a => a.CreatedAt) // Sắp xếp theo thời gian tạo mới nhất trước
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<Appointment?> GetByIdAsync(string id)
        {
            return await _context.Appointments
                
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AppointmentId == id);
        }

        public async Task<Appointment> CreateAsync(Appointment entity)
        {
            _context.Appointments.Add(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public async Task UpdateAsync(Appointment entity)
        {
            _context.Appointments.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Appointment entity)
        {
            _context.Appointments.Remove(entity);
            await _context.SaveChangesAsync();
        }
        public async Task<IEnumerable<Appointment>> GetByPatientIdAsync(string patientId)
        {
            return await _context.Appointments
                
                .AsNoTracking()
                .Where(a => a.CreatedBy == patientId)
                .ToListAsync();
        }
    }
}
