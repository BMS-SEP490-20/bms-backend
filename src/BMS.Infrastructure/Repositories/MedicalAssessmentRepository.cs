using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Medical Assessment (UC50 / Iteration 3).
/// Persists clinical assessments to MongoDB collection <c>medical_assessments</c>
/// with in-memory synchronization to <see cref="BmsDbContext"/>.
/// </summary>
public class MedicalAssessmentRepository : IMedicalAssessmentRepository
{
    private readonly IMongoCollection<MedicalAssessment>? _collection;
    private readonly BmsDbContext _dbContext;

    public MedicalAssessmentRepository(IMongoDatabase? database, BmsDbContext dbContext)
    {
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _collection = database.GetCollection<MedicalAssessment>("medical_assessments");
            }
            catch
            {
                _collection = null;
            }
        }
    }

    public async Task<MedicalAssessment> CreateAsync(MedicalAssessment assessment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(assessment.Id))
        {
            assessment.Id = Guid.NewGuid().ToString();
        }
        if (assessment.CreatedAt == default)
        {
            assessment.CreatedAt = DateTime.UtcNow;
        }

        if (_collection != null)
        {
            try
            {
                await _collection.InsertOneAsync(assessment, cancellationToken: ct);
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.MedicalAssessments)
        {
            _dbContext.MedicalAssessments.Add(assessment);
        }

        return assessment;
    }

    public async Task<MedicalAssessment?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (_collection != null)
        {
            try
            {
                var item = await _collection.Find(x => (x.Id == id || x.AssessmentCode == id) && !x.IsDeleted).FirstOrDefaultAsync(ct);
                if (item != null) return item;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.MedicalAssessments)
        {
            return _dbContext.MedicalAssessments.FirstOrDefault(x => (x.Id == id || x.AssessmentCode == id) && !x.IsDeleted);
        }
    }

    public async Task<MedicalAssessment?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        if (_collection != null)
        {
            try
            {
                var item = await _collection.Find(x => x.AssessmentCode == code && !x.IsDeleted).FirstOrDefaultAsync(ct);
                if (item != null) return item;
            }
            catch
            {
                // Fall back
            }
        }

        lock (_dbContext.MedicalAssessments)
        {
            return _dbContext.MedicalAssessments.FirstOrDefault(x => x.AssessmentCode == code && !x.IsDeleted);
        }
    }

    public async Task<List<MedicalAssessment>> GetByPatientIdAsync(string patientId, CancellationToken ct = default)
    {
        if (_collection != null)
        {
            try
            {
                var list = await _collection.Find(x => (x.PatientId == patientId || x.PatientCode == patientId) && !x.IsDeleted)
                                            .SortByDescending(x => x.CreatedAt)
                                            .ToListAsync(ct);
                if (list != null && list.Count > 0) return list;
            }
            catch
            {
                // Fall back
            }
        }

        lock (_dbContext.MedicalAssessments)
        {
            return _dbContext.MedicalAssessments
                .Where(x => (x.PatientId == patientId || x.PatientCode == patientId) && !x.IsDeleted)
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }
    }

    public async Task<List<MedicalAssessment>> GetByEncounterIdAsync(string encounterId, CancellationToken ct = default)
    {
        if (_collection != null)
        {
            try
            {
                var list = await _collection.Find(x => x.EncounterId == encounterId && !x.IsDeleted)
                                            .SortByDescending(x => x.CreatedAt)
                                            .ToListAsync(ct);
                if (list != null && list.Count > 0) return list;
            }
            catch
            {
                // Fall back
            }
        }

        lock (_dbContext.MedicalAssessments)
        {
            return _dbContext.MedicalAssessments
                .Where(x => x.EncounterId == encounterId && !x.IsDeleted)
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }
    }

    public async Task<(List<MedicalAssessment> Items, int TotalCount)> QueryAsync(
        string? patientId = null,
        string? doctorId = null,
        string? status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? keyword = null,
        int skip = 0,
        int limit = 20,
        CancellationToken ct = default)
    {
        // Try MongoDB first
        if (_collection != null)
        {
            try
            {
                var builder = Builders<MedicalAssessment>.Filter;
                var filter = builder.Eq(x => x.IsDeleted, false);

                if (!string.IsNullOrWhiteSpace(patientId))
                {
                    filter &= builder.Or(
                        builder.Eq(x => x.PatientId, patientId),
                        builder.Eq(x => x.PatientCode, patientId));
                }

                if (!string.IsNullOrWhiteSpace(doctorId))
                {
                    filter &= builder.Eq(x => x.DoctorId, doctorId);
                }

                if (!string.IsNullOrWhiteSpace(status))
                {
                    filter &= builder.Eq(x => x.Status, status);
                }

                if (fromDate.HasValue)
                {
                    filter &= builder.Gte(x => x.AssessmentDate, fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    filter &= builder.Lte(x => x.AssessmentDate, toDate.Value);
                }

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    var regex = new MongoDB.Bson.BsonRegularExpression(keyword, "i");
                    filter &= builder.Or(
                        builder.Regex(x => x.AssessmentCode, regex),
                        builder.Regex(x => x.PatientName, regex),
                        builder.Regex(x => x.Icd10Code, regex),
                        builder.Regex(x => x.FinalDiagnosis, regex),
                        builder.Regex(x => x.ChiefComplaint, regex));
                }

                var total = (int)await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
                var items = await _collection.Find(filter)
                    .SortByDescending(x => x.CreatedAt)
                    .Skip(skip)
                    .Limit(limit)
                    .ToListAsync(ct);

                return (items, total);
            }
            catch
            {
                // Fall back to memory
            }
        }

        // In-memory fallback
        lock (_dbContext.MedicalAssessments)
        {
            var query = _dbContext.MedicalAssessments.Where(x => !x.IsDeleted).AsQueryable();

            if (!string.IsNullOrWhiteSpace(patientId))
            {
                query = query.Where(x => x.PatientId == patientId || x.PatientCode == patientId);
            }

            if (!string.IsNullOrWhiteSpace(doctorId))
            {
                query = query.Where(x => x.DoctorId == doctorId);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            if (fromDate.HasValue)
            {
                query = query.Where(x => x.AssessmentDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(x => x.AssessmentDate <= toDate.Value);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLowerInvariant();
                query = query.Where(x =>
                    (!string.IsNullOrEmpty(x.AssessmentCode) && x.AssessmentCode.ToLower().Contains(kw)) ||
                    (!string.IsNullOrEmpty(x.PatientName) && x.PatientName.ToLower().Contains(kw)) ||
                    (!string.IsNullOrEmpty(x.Icd10Code) && x.Icd10Code.ToLower().Contains(kw)) ||
                    (!string.IsNullOrEmpty(x.FinalDiagnosis) && x.FinalDiagnosis.ToLower().Contains(kw)) ||
                    (!string.IsNullOrEmpty(x.ChiefComplaint) && x.ChiefComplaint.ToLower().Contains(kw)));
            }

            var total = query.Count();
            var items = query.OrderByDescending(x => x.CreatedAt)
                             .Skip(skip)
                             .Take(limit)
                             .ToList();

            return (items, total);
        }
    }

    public async Task<MedicalAssessment> UpdateAsync(MedicalAssessment assessment, CancellationToken ct = default)
    {
        assessment.UpdatedAt = DateTime.UtcNow;

        if (_collection != null)
        {
            try
            {
                await _collection.ReplaceOneAsync(x => x.Id == assessment.Id, assessment, cancellationToken: ct);
            }
            catch
            {
                // Fall back
            }
        }

        lock (_dbContext.MedicalAssessments)
        {
            var idx = _dbContext.MedicalAssessments.FindIndex(x => x.Id == assessment.Id);
            if (idx >= 0)
            {
                _dbContext.MedicalAssessments[idx] = assessment;
            }
            else
            {
                _dbContext.MedicalAssessments.Add(assessment);
            }
        }

        return assessment;
    }

    public async Task<bool> SoftDeleteAsync(string id, string deletedBy, string deleteReason, CancellationToken ct = default)
    {
        var target = await GetByIdAsync(id, ct);
        if (target == null) return false;

        target.IsDeleted = true;
        target.DeletedAt = DateTime.UtcNow;
        target.DeletedBy = deletedBy;
        target.DeleteReason = deleteReason;

        if (_collection != null)
        {
            try
            {
                var update = Builders<MedicalAssessment>.Update
                    .Set(x => x.IsDeleted, true)
                    .Set(x => x.DeletedAt, target.DeletedAt)
                    .Set(x => x.DeletedBy, target.DeletedBy)
                    .Set(x => x.DeleteReason, target.DeleteReason);
                await _collection.UpdateOneAsync(x => x.Id == target.Id, update, cancellationToken: ct);
            }
            catch
            {
                // Fall back
            }
        }

        lock (_dbContext.MedicalAssessments)
        {
            var mem = _dbContext.MedicalAssessments.FirstOrDefault(x => x.Id == target.Id);
            if (mem != null)
            {
                mem.IsDeleted = true;
                mem.DeletedAt = target.DeletedAt;
                mem.DeletedBy = target.DeletedBy;
                mem.DeleteReason = target.DeleteReason;
            }
        }

        return true;
    }

    public async Task<string> GenerateNextAssessmentCodeAsync(DateTime date, CancellationToken ct = default)
    {
        var datePrefix = $"ASM-{date:yyMMdd}";
        var count = 0;

        if (_collection != null)
        {
            try
            {
                var regex = new MongoDB.Bson.BsonRegularExpression($"^{datePrefix}");
                count = (int)await _collection.CountDocumentsAsync(x => x.AssessmentCode.StartsWith(datePrefix), cancellationToken: ct);
            }
            catch
            {
                count = 0;
            }
        }

        lock (_dbContext.MedicalAssessments)
        {
            var memCount = _dbContext.MedicalAssessments.Count(x => x.AssessmentCode.StartsWith(datePrefix));
            if (memCount > count) count = memCount;
        }

        return $"{datePrefix}-{(count + 1):D4}";
    }
}
