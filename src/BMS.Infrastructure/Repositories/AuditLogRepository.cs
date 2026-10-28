using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Testorder_service.Data;
using Testorder_service.Models.DTO;
using Testorder_service.Models.Entities;
using Testorder_service.Repositories.Interface;

namespace Testorder_service.Repositories.Implement
{
    public class AuditLogRepository : IAuditLogRepository
    {
        private readonly TestOrderManagementDbContext _context;

        public AuditLogRepository(TestOrderManagementDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<AuditLog>> GetAllAsync(AuditLogFilterDto filter)
        {
            var query = _context.AuditLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Category) && filter.Category != "ALL")
            {
                query = query.Where(a => a.Category == filter.Category);
            }

            if (!string.IsNullOrWhiteSpace(filter.Severity) && filter.Severity != "ALL")
            {
                query = query.Where(a => a.Severity == filter.Severity);
            }

            if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "ALL")
            {
                query = query.Where(a => a.Status == filter.Status);
            }

            if (!string.IsNullOrWhiteSpace(filter.Action) && filter.Action != "ALL")
            {
                query = query.Where(a => a.Action == filter.Action);
            }

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(a => a.UserName.ToLower().Contains(term) ||
                                         a.Action.ToLower().Contains(term) ||
                                         a.Details.ToLower().Contains(term) ||
                                         a.TargetEntity.ToLower().Contains(term));
            }

            if (filter.FromDate.HasValue)
            {
                query = query.Where(a => a.Timestamp >= filter.FromDate.Value);
            }

            if (filter.ToDate.HasValue)
            {
                query = query.Where(a => a.Timestamp <= filter.ToDate.Value);
            }

            var skip = (Math.Max(1, filter.PageNumber) - 1) * Math.Max(1, filter.PageSize);
            return await query.OrderByDescending(a => a.Timestamp)
                              .Skip(skip)
                              .Take(Math.Max(1, filter.PageSize))
                              .ToListAsync();
        }

        public async Task<int> GetCountAsync(AuditLogFilterDto filter)
        {
            var query = _context.AuditLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Category) && filter.Category != "ALL")
                query = query.Where(a => a.Category == filter.Category);

            if (!string.IsNullOrWhiteSpace(filter.Severity) && filter.Severity != "ALL")
                query = query.Where(a => a.Severity == filter.Severity);

            if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "ALL")
                query = query.Where(a => a.Status == filter.Status);

            if (!string.IsNullOrWhiteSpace(filter.Action) && filter.Action != "ALL")
                query = query.Where(a => a.Action == filter.Action);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(a => a.UserName.ToLower().Contains(term) ||
                                         a.Action.ToLower().Contains(term) ||
                                         a.Details.ToLower().Contains(term) ||
                                         a.TargetEntity.ToLower().Contains(term));
            }

            if (filter.FromDate.HasValue)
                query = query.Where(a => a.Timestamp >= filter.FromDate.Value);

            if (filter.ToDate.HasValue)
                query = query.Where(a => a.Timestamp <= filter.ToDate.Value);

            return await query.CountAsync();
        }

        public async Task<AuditLog?> GetByIdAsync(string id)
        {
            return await _context.AuditLogs.FirstOrDefaultAsync(a => a.AuditId == id);
        }

        public async Task AddAsync(AuditLog entity)
        {
            await _context.AuditLogs.AddAsync(entity);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
