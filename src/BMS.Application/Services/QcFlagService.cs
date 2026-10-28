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
    /// Service managing Quality Control (QC) flags, analyzer rules, and resolution states.
    /// Operates directly on live MongoDB records without hardcoded seed data.
    /// </summary>
    public class QcFlagService : IQcFlagService
    {
        private readonly IQcFlagRepository _repository;

        public QcFlagService(IQcFlagRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Retrieves filtered QC flags from MongoDB based on status, severity, analyzer, or parameter.
        /// </summary>
        public async Task<IEnumerable<QcFlag>> GetAllAsync(string? status, string? severity, string? analyzerId, string? parameter)
        {
            return await _repository.GetAllAsync(status, severity, analyzerId, parameter);
        }

        /// <summary>
        /// Retrieves a single QC flag record by identifier.
        /// </summary>
        public async Task<QcFlag?> GetByIdAsync(string id)
        {
            return await _repository.GetByIdAsync(id);
        }

        /// <summary>
        /// Creates a new QC flag triggered by an analyzer threshold rule or manual inspection.
        /// </summary>
        public async Task<QcFlag> CreateAsync(CreateQcFlagDto dto)
        {
            var flag = new QcFlag
            {
                FlagId = $"QC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}",
                FlagCode = !string.IsNullOrWhiteSpace(dto.FlagCode) ? dto.FlagCode : $"QC-ERR-{new Random().Next(100, 999)}",
                AnalyzerId = dto.AnalyzerId,
                AnalyzerName = dto.AnalyzerName,
                Parameter = dto.Parameter,
                RuleType = dto.RuleType,
                Severity = dto.Severity,
                Status = dto.Status,
                ObservedValue = dto.ObservedValue,
                TargetValue = dto.TargetValue,
                LowerLimit = dto.LowerLimit,
                UpperLimit = dto.UpperLimit,
                Unit = dto.Unit,
                SampleBarcode = dto.SampleBarcode,
                TriggeredAt = DateTime.UtcNow,
                TriggeredBy = dto.TriggeredBy ?? "Kỹ thuật viên phòng Lab",
                ResolutionNotes = dto.ResolutionNotes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _repository.AddAsync(flag);
            await _repository.SaveChangesAsync();
            return flag;
        }

        /// <summary>
        /// Updates resolution details, severity, or status of an existing QC flag.
        /// </summary>
        public async Task<QcFlag?> UpdateAsync(string id, UpdateQcFlagDto dto)
        {
            var existing = await _repository.GetByIdAsync(id);
            if (existing == null) return null;

            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                existing.Status = dto.Status;
                // Side effect: record resolution timestamp and technician upon resolving/dismissing
                if (dto.Status == "RESOLVED" || dto.Status == "DISMISSED")
                {
                    existing.ResolvedAt = DateTime.UtcNow;
                    existing.ResolvedBy = dto.ResolvedBy ?? "Kỹ thuật viên Lab";
                }
            }

            if (!string.IsNullOrWhiteSpace(dto.Severity)) existing.Severity = dto.Severity;
            if (!string.IsNullOrWhiteSpace(dto.ResolutionNotes)) existing.ResolutionNotes = dto.ResolutionNotes;
            if (dto.ObservedValue.HasValue) existing.ObservedValue = dto.ObservedValue.Value;
            if (dto.TargetValue.HasValue) existing.TargetValue = dto.TargetValue.Value;
            if (dto.LowerLimit.HasValue) existing.LowerLimit = dto.LowerLimit.Value;
            if (dto.UpperLimit.HasValue) existing.UpperLimit = dto.UpperLimit.Value;

            existing.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateAsync(existing);
            await _repository.SaveChangesAsync();
            return existing;
        }

        /// <summary>
        /// Deletes a QC flag record from MongoDB.
        /// </summary>
        public async Task<bool> DeleteAsync(string id)
        {
            var existing = await _repository.GetByIdAsync(id);
            if (existing == null) return false;

            await _repository.DeleteAsync(existing);
            await _repository.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Computes real-time QC metrics based on actual records in MongoDB.
        /// </summary>
        public async Task<object> GetStatisticsAsync()
        {
            var all = (await _repository.GetAllAsync(null, null, null, null)).ToList();

            var total = all.Count;
            var active = all.Count(q => q.Status == "ACTIVE");
            var investigating = all.Count(q => q.Status == "INVESTIGATING");
            var resolved = all.Count(q => q.Status == "RESOLVED");
            var critical = all.Count(q => q.Severity == "CRITICAL");

            return new
            {
                totalFlags = total,
                activeFlags = active,
                investigatingFlags = investigating,
                resolvedFlags = resolved,
                criticalFlags = critical
            };
        }
    }
}
