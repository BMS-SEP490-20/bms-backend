using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Threading;
using BMS.Application.DTOs.QC;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;

namespace BMS.Application.Services
{
    /// <summary>
    /// Service managing Quality Control (QC) flags, analyzer rules, and resolution states.
    /// Operates directly on live MongoDB records without hardcoded seed data.
    /// </summary>
    public class QcFlagService : IQcFlagService
    {
        private readonly IQcFlagRepository _repository;

        /// <summary>Creates the service over the QC flag repository.</summary>
        public QcFlagService(IQcFlagRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Retrieves filtered QC flags from MongoDB based on status, severity, analyzer, or parameter.
        /// </summary>
        public Task<IReadOnlyList<QcFlag>> GetAllAsync(string? status, string? severity, string? analyzerId, string? parameter, CancellationToken ct = default)
            => _repository.GetAllAsync(status, severity, analyzerId, parameter, ct);

        /// <summary>
        /// Retrieves a single QC flag record by identifier.
        /// </summary>
        public Task<QcFlag?> GetByIdAsync(string id, CancellationToken ct = default) => _repository.GetByIdAsync(id, ct);

        /// <summary>
        /// Creates a new QC flag triggered by an analyzer threshold rule or manual inspection.
        /// </summary>
        public async Task<QcFlag> CreateAsync(CreateQcFlagDto dto, CancellationToken ct = default)
        {
            var flag = new QcFlag
            {
                FlagId = $"QC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}",
                FlagCode = !string.IsNullOrWhiteSpace(dto.FlagCode) ? dto.FlagCode : $"QC-ERR-{RandomNumberGenerator.GetInt32(100, 1000)}",
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

            await _repository.AddAsync(flag, ct);
            return flag;
        }

        /// <summary>
        /// Updates resolution details, severity, or status of an existing QC flag.
        /// </summary>
        public async Task<QcFlag?> UpdateAsync(string id, UpdateQcFlagDto dto, CancellationToken ct = default)
        {
            var existing = await _repository.GetByIdAsync(id, ct);
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

            return await _repository.UpdateAsync(existing, ct) ? existing : null;
        }

        /// <summary>
        /// Deletes a QC flag record from MongoDB.
        /// </summary>
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => _repository.DeleteAsync(id, ct);

        /// <summary>
        /// Computes real-time QC metrics based on actual records in MongoDB.
        /// </summary>
        public async Task<QcFlagStatisticsDto> GetStatisticsAsync(CancellationToken ct = default)
        {
            // Server-side counts instead of loading every flag into memory.
            return new QcFlagStatisticsDto(
                await _repository.CountAsync(ct: ct),
                await _repository.CountAsync(status: "ACTIVE", ct: ct),
                await _repository.CountAsync(status: "INVESTIGATING", ct: ct),
                await _repository.CountAsync(status: "RESOLVED", ct: ct),
                await _repository.CountAsync(severity: "CRITICAL", ct: ct));
        }
    }
}
