using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.AuditLogs;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;

namespace BMS.Application.Services
{
    /// <summary>
    /// Service managing operational, security, and access audit logs.
    /// Operates purely on live MongoDB audit collections without dummy seeds.
    /// </summary>
    public class AuditLogService : IAuditLogService
    {
        private readonly IAuditLogRepository _repository;

        /// <summary>Creates the service over the audit log repository.</summary>
        public AuditLogService(IAuditLogRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Retrieves paginated audit logs based on the provided filter criteria.
        /// </summary>
        public async Task<(IReadOnlyList<AuditLog> Logs, long TotalCount)> GetAuditLogsAsync(AuditLogFilterDto filter, CancellationToken ct = default)
        {
            var query = filter.ToQuery();
            var logs = await _repository.GetAllAsync(query, ct);
            var count = await _repository.GetCountAsync(query, ct);
            return (logs, count);
        }

        /// <summary>
        /// Retrieves an individual audit log by its unique record ID.
        /// </summary>
        public Task<AuditLog?> GetByIdAsync(string id, CancellationToken ct = default) => _repository.GetByIdAsync(id, ct);

        /// <summary>
        /// Inserts an immutable audit record capturing system or user actions.
        /// </summary>
        public async Task<AuditLog> LogAsync(CreateAuditLogDto dto, CancellationToken ct = default)
        {
            // Business rule: timestamps and unique log IDs are generated on the server for non-repudiation.
            var log = new AuditLog
            {
                AuditId = $"AUD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                Timestamp = DateTime.UtcNow,
                UserId = dto.UserId ?? "SYSTEM",
                UserName = dto.UserName ?? "Hệ thống",
                UserRole = dto.UserRole ?? "System",
                Action = dto.Action,
                Category = dto.Category,
                Severity = dto.Severity,
                IpAddress = dto.IpAddress ?? "unknown",
                TargetEntity = dto.TargetEntity ?? "System",
                EntityId = dto.EntityId,
                Details = dto.Details,
                Status = dto.Status,
                // Record only what the client actually sent; never invent a user agent.
                Browser = dto.Browser
            };

            await _repository.AddAsync(log, ct);
            return log;
        }

        /// <summary>
        /// Computes live audit metric counters directly from MongoDB (server-side counts, no 1000-row cap).
        /// </summary>
        public Task<AuditLogStatistics> GetStatisticsAsync(CancellationToken ct = default) => _repository.GetStatisticsAsync(ct);
    }
}
