using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB & BmsDbContext implementation of ITestResultVerificationRepository (UC72 / Verify Test Results).
/// Handles persistence of laboratory technical verification checklists.
/// </summary>
public class TestResultVerificationRepository : ITestResultVerificationRepository
{
    private readonly IMongoCollection<TestResultVerification>? _collection;
    private readonly BmsDbContext _dbContext;

    public TestResultVerificationRepository(IMongoDatabase? database, BmsDbContext dbContext)
    {
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _collection = database.GetCollection<TestResultVerification>("test_verifications");
            }
            catch
            {
                // In-memory fallback
            }
        }
    }

    public async Task<TestResultVerification?> GetBySpecimenIdAsync(string specimenId)
    {
        if (_collection != null)
        {
            try
            {
                var item = await _collection
                    .Find(v => v.SpecimenId.Equals(specimenId, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefaultAsync();
                if (item != null) return item;
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.TestVerifications)
        {
            return _dbContext.TestVerifications.FirstOrDefault(v =>
                v.SpecimenId.Equals(specimenId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<IEnumerable<TestResultVerification>> GetPendingQueueAsync()
    {
        if (_collection != null)
        {
            try
            {
                var list = await _collection
                    .Find(v => v.Status == "PendingVerification")
                    .SortByDescending(v => v.CreatedAt)
                    .ToListAsync();
                if (list != null && list.Any())
                {
                    return list;
                }
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.TestVerifications)
        {
            return _dbContext.TestVerifications
                .Where(v => v.Status == "PendingVerification")
                .OrderByDescending(v => v.CreatedAt)
                .ToList();
        }
    }

    public async Task<bool> UpsertVerificationAsync(TestResultVerification verification)
    {
        if (_collection != null)
        {
            try
            {
                await _collection.ReplaceOneAsync(
                    v => v.SpecimenId == verification.SpecimenId,
                    verification,
                    new ReplaceOptions { IsUpsert = true });
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.TestVerifications)
        {
            var idx = _dbContext.TestVerifications.FindIndex(v => v.SpecimenId == verification.SpecimenId);
            if (idx >= 0)
            {
                _dbContext.TestVerifications[idx] = verification;
            }
            else
            {
                _dbContext.TestVerifications.Add(verification);
            }
        }

        return await Task.FromResult(true);
    }
}
