using BMS.Core.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Threading;
using BMS.Application.DTOs.Notifications;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;

namespace BMS.Application.Services
{
    /// <summary>
    /// Service managing notification retrieval, lifecycle, and dispatching.
    /// Operates purely on real MongoDB notification records triggered by system events.
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _repo;
        private readonly ILogger<NotificationService> _logger;

        /// <summary>Creates the service over the notification repository.</summary>
        public NotificationService(INotificationRepository repo, ILogger<NotificationService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves past notifications for a specific user from MongoDB.
        /// </summary>
        public async Task<IReadOnlyList<NotificationDto>> GetNotificationsForUserAsync(string userId, string? role, CancellationToken ct = default)
        {
            var normalizedRole = NormalizeRoleName(role);
            var entities = await _repo.GetByUserAndRoleAsync(userId, normalizedRole, ct);
            return entities.Select(MapToDto).ToList();
        }

        /// <summary>
        /// Retrieves notifications broadcasted or targeted to a specific role.
        /// </summary>
        public async Task<IReadOnlyList<NotificationDto>> GetNotificationsByRoleAsync(string role, CancellationToken ct = default)
        {
            var normalizedRole = NormalizeRoleName(role);
            var entities = await _repo.GetByRoleAsync(normalizedRole, ct);
            return entities.Select(MapToDto).ToList();
        }

        /// <summary>
        /// Marks a single notification as read by the recipient.
        /// </summary>
        public Task<bool> MarkAsReadAsync(string notificationId, CancellationToken ct = default) =>
            _repo.MarkAsReadAsync(notificationId, DateTime.UtcNow, ct);

        /// <summary>
        /// Marks all unread notifications for a user as read in bulk.
        /// </summary>
        public async Task<bool> MarkAllAsReadAsync(string userId, CancellationToken ct = default)
        {
            // Single UpdateMany instead of one update per notification (no N+1 writes).
            await _repo.MarkAllAsReadAsync(userId, DateTime.UtcNow, ct);
            return true;
        }

        /// <summary>
        /// Creates and dispatches a real notification triggered by domain actions.
        /// </summary>
        public async Task<NotificationDto> CreateNotificationAsync(CreateNotificationDto dto, CancellationToken ct = default)
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

            await _repo.AddAsync(entity, ct);

            return MapToDto(entity);
        }

        /// <summary>
        /// Normalizes role aliases into system standard role identifiers.
        /// </summary>
        // Đợt 2: numeric IDs and legacy names map through the shared 6-role constants (1 Admin, 2 Manager,
        // 3 Lab, 4 Doctor, 5 Receptionist, 6 Patient). Unknown audiences are kept as-is (e.g. a custom group).
        private static string NormalizeRoleName(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return RoleNames.Patient;
            var r = role.Trim();
            if (int.TryParse(r, out var id)) return RoleNames.FromRoleId(id) ?? role;
            return r.ToLowerInvariant() switch
            {
                "labmanager" or "lab_manager" => RoleNames.Manager,
                "laboratory user" or "technician" => RoleNames.Lab,
                "staff" => RoleNames.Receptionist,
                _ => RoleNames.Normalize(r) ?? role
            };
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
