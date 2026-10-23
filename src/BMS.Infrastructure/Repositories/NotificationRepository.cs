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
    /// Repository implementation providing MongoDB data access for notification records.
    /// </summary>
    public class NotificationRepository : INotificationRepository
    {
        private readonly TestOrderManagementDbContext _context;

        public NotificationRepository(TestOrderManagementDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Notification>> GetByUserIdAsync(string userId)
        {
            return await _context.Notifications
                .Where(n => n.UserId == userId || n.UserId == "ALL")
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Notification>> GetByRoleAsync(string role)
        {
            return await _context.Notifications
                .Where(n => n.Role == role || n.Role == "All")
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Notification>> GetByUserAndRoleAsync(string userId, string? role)
        {
            var query = _context.Notifications.AsQueryable();
            if (!string.IsNullOrWhiteSpace(role))
            {
                var normalizedRole = role.Trim();
                query = query.Where(n => n.UserId == userId || n.UserId == "ALL" || n.Role == normalizedRole || n.Role == "All");
            }
            else
            {
                query = query.Where(n => n.UserId == userId || n.UserId == "ALL");
            }
            return await query.OrderByDescending(n => n.CreatedAt).ToListAsync();
        }

        public async Task<Notification?> GetByIdAsync(string id)
        {
            return await _context.Notifications.FirstOrDefaultAsync(n => n.NotificationId == id);
        }

        public async Task AddAsync(Notification entity)
        {
            await _context.Notifications.AddAsync(entity);
        }

        public Task UpdateAsync(Notification entity)
        {
            _context.Notifications.Update(entity);
            return Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
