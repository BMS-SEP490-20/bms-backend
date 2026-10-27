using Microsoft.EntityFrameworkCore;
using TestOrder.Repositories.Interface;
using Testorder_service.Data;
using Testorder_service.Models.Entities;
using Testorder_service.Repositories.Interface;

namespace TestOrder.Repositories.Implement
{
    public class LabRoomRepository : ILabRoomRepository
    {
        private readonly TestOrderManagementDbContext _context;

        public LabRoomRepository(TestOrderManagementDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<LabRoom>> GetAllAsync()
        {
            return await _context.LabRooms.ToListAsync();
        }

        public async Task<LabRoom?> GetByIdAsync(string id)
        {
            return await _context.LabRooms.FindAsync(id);
        }

        public async Task AddAsync(LabRoom room)
        {
            await _context.LabRooms.AddAsync(room);
        }

        public async Task UpdateAsync(LabRoom room)
        {
            _context.LabRooms.Update(room);
            await Task.CompletedTask;
        }

        public async Task DeleteAsync(LabRoom room)
        {
            _context.LabRooms.Remove(room);
            await Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
