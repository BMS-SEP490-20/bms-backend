using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Testorder_service.Models.DTO;
using Testorder_service.Models.Entities;
using Testorder_service.Repositories.Interface;
using Testorder_service.Services.Interface;

namespace Testorder_service.Services.Implement
{
    /// <summary>
    /// Service managing operational, security, and access audit logs.
    /// Operates purely on live MongoDB audit collections without dummy seeds.
    /// </summary>
    public class AuditLogService : IAuditLogService
    {
        private readonly IAuditLogRepository _repository;

        public AuditLogService(IAuditLogRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Retrieves paginated audit logs based on the provided filter criteria.
        /// </summary>
        public async Task<(IEnumerable<AuditLog> Logs, int TotalCount)> GetAuditLogsAsync(AuditLogFilterDto filter)
        {
            var logs = await _repository.GetAllAsync(filter);
            var count = await _repository.GetCountAsync(filter);
            return (logs, count);
        }

        /// <summary>
        /// Retrieves an individual audit log by its unique record ID.
        /// </summary>
        public async Task<AuditLog?> GetByIdAsync(string id)
        {
            return await _repository.GetByIdAsync(id);
        }

        /// <summary>
        /// Inserts an immutable audit record capturing system or user actions.
        /// </summary>
        public async Task<AuditLog> LogAsync(CreateAuditLogDto dto)
        {
            // Business rule: timestamps and unique log IDs are generated on the server for non-repudiation.
            var log = new AuditLog
            {
                AuditId = $"AUD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..6].ToUpper()}",
                Timestamp = DateTime.UtcNow,
                UserId = dto.UserId ?? "SYSTEM",
                UserName = dto.UserName ?? "Hệ thống",
                UserRole = dto.UserRole ?? "System",
                Action = dto.Action,
                Category = dto.Category,
                Severity = dto.Severity,
                IpAddress = dto.IpAddress ?? "127.0.0.1",
                TargetEntity = dto.TargetEntity ?? "System",
                EntityId = dto.EntityId,
                Details = dto.Details,
                Status = dto.Status,
                Browser = dto.Browser ?? "Web Browser (Chrome/124.0)"
            };

            await _repository.AddAsync(log);
            await _repository.SaveChangesAsync();
            return log;
        }

        /// <summary>
        /// Computes live audit metric counters directly from MongoDB.
        /// </summary>
        public async Task<object> GetStatisticsAsync()
        {
            var all = (await _repository.GetAllAsync(new AuditLogFilterDto { PageSize = 1000 })).ToList();

            var total = all.Count;
            var securityCount = all.Count(x => x.Category == "SECURITY");
            var accessCount = all.Count(x => x.Category == "ACCESS");
            var criticalCount = all.Count(x => x.Severity == "CRITICAL" || x.Severity == "ERROR");
            var failedCount = all.Count(x => x.Status == "FAILED");

            return new
            {
                totalLogs = total,
                securityEvents = securityCount,
                accessEvents = accessCount,
                criticalAlerts = criticalCount,
                failedActions = failedCount
            };
        }
    }
}
