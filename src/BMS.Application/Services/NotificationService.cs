using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Testorder_service.Models.DTO;
using Testorder_service.Models.Entities;
using Testorder_service.Repositories.Interface;
using Testorder_service.Services.Interface;

namespace Testorder_service.Services.Implement
{
    /// <summary>
    /// Service managing notification retrieval, lifecycle, and dispatching.
    /// Operates purely on real MongoDB notification records triggered by system events.
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _repo;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(INotificationRepository repo, ILogger<NotificationService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves past notifications for a specific user from MongoDB.
        /// </summary>
        public async Task<IEnumerable<NotificationDto>> GetNotificationsForUserAsync(string userId, string? role)
        {
            var normalizedRole = NormalizeRoleName(role);
            var entities = (await _repo.GetByUserAndRoleAsync(userId, normalizedRole)).ToList();
            return entities.Select(MapToDto);
        }

        /// <summary>
        /// Retrieves notifications broadcasted or targeted to a specific role.
        /// </summary>
        public async Task<IEnumerable<NotificationDto>> GetNotificationsByRoleAsync(string role)
        {
            var normalizedRole = NormalizeRoleName(role);
            var entities = await _repo.GetByRoleAsync(normalizedRole);
            return entities.Select(MapToDto);
        }

        /// <summary>
        /// Marks a single notification as read by the recipient.
        /// </summary>
        public async Task<bool> MarkAsReadAsync(string notificationId)
        {
            var entity = await _repo.GetByIdAsync(notificationId);
            if (entity == null) return false;

            entity.IsRead = true;
            entity.ReadAt = DateTime.UtcNow;

            await _repo.UpdateAsync(entity);
            await _repo.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Marks all unread notifications for a user as read in bulk.
        /// </summary>
        public async Task<bool> MarkAllAsReadAsync(string userId)
        {
            var list = await _repo.GetByUserIdAsync(userId);
            foreach (var item in list)
            {
                item.IsRead = true;
                item.ReadAt = DateTime.UtcNow;
                await _repo.UpdateAsync(item);
            }
            await _repo.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Creates and dispatches a real notification triggered by domain actions.
        /// </summary>
        public async Task<NotificationDto> CreateNotificationAsync(CreateNotificationDto dto)
        {
            var randomSuffix = Guid.NewGuid().ToString("N")[..8].ToUpper();
            var entity = new Notification
            {
                NotificationId = $"NOT{randomSuffix}",
                UserId = dto.UserId,
                Role = NormalizeRoleName(dto.Role),
                Title = dto.Title,
                Message = dto.Message,
                Type = dto.Type,
                ReferenceId = dto.ReferenceId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _repo.AddAsync(entity);
            await _repo.SaveChangesAsync();

            return MapToDto(entity);
        }

        /// <summary>
        /// Normalizes role aliases into system standard role identifiers.
        /// </summary>
        private static string NormalizeRoleName(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return "Patient";
            var r = role.Trim().ToLowerInvariant();
            if (r == "1" || r == "admin" || r == "administrator") return "Admin";
            if (r == "2" || r == "manager" || r == "labmanager" || r == "lab_manager") return "Manager";
            if (r == "3" || r == "doctor") return "Doctor";
            if (r == "4" || r == "labuser" || r == "laboratory user" || r == "lab_user" || r == "technician") return "LabUser";
            if (r == "5" || r == "patient") return "Patient";
            if (r == "6" || r == "receptionist" || r == "staff") return "Receptionist";
            return role;
        }

        /// <summary>
        /// Maps an entity to public DTO contract.
        /// </summary>
        private static NotificationDto MapToDto(Notification entity)
        {
            return new NotificationDto
            {
                NotificationId = entity.NotificationId,
                UserId = entity.UserId,
                Role = entity.Role,
                Title = entity.Title,
                Message = entity.Message,
                Type = entity.Type,
                ReferenceId = entity.ReferenceId,
                IsRead = entity.IsRead,
                CreatedAt = entity.CreatedAt,
                ReadAt = entity.ReadAt
            };
        }
    }
}
