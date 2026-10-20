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
/// MongoDB & BmsDbContext implementation of ISampleOriginTraceRepository (UC77 / Trace Sample Origin).
/// </summary>
public class SampleOriginTraceRepository : ISampleOriginTraceRepository
{
    private readonly IMongoCollection<SampleOriginTrace>? _collection;
    private readonly BmsDbContext _dbContext;

    public SampleOriginTraceRepository(IMongoDatabase? database, BmsDbContext dbContext)
    {
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _collection = database.GetCollection<SampleOriginTrace>("sample_origin_traces");
            }
            catch
            {
                // Fallback
            }
        }
    }

    public async Task<SampleOriginTrace?> GetBySpecimenIdAsync(string specimenId)
    {
        if (_collection != null)
        {
            try
            {
                var item = await _collection
                    .Find(t => t.SpecimenId.Equals(specimenId, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefaultAsync();
                if (item != null) return item;
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.SampleOriginTraces)
        {
            return _dbContext.SampleOriginTraces.FirstOrDefault(t =>
                t.SpecimenId.Equals(specimenId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<IEnumerable<SampleOriginTrace>> GetRecentTracesAsync(int limit = 10)
    {
        if (_collection != null)
        {
            try
            {
                var list = await _collection
                    .Find(_ => true)
                    .SortByDescending(t => t.CreatedAt)
                    .Limit(limit)
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

        lock (_dbContext.SampleOriginTraces)
        {
            return _dbContext.SampleOriginTraces
                .OrderByDescending(t => t.CreatedAt)
                .Take(limit)
                .ToList();
        }
    }

    public async Task<bool> UpsertTraceAsync(SampleOriginTrace trace)
    {
        if (_collection != null)
        {
            try
            {
                await _collection.ReplaceOneAsync(
                    t => t.SpecimenId == trace.SpecimenId,
                    trace,
                    new ReplaceOptions { IsUpsert = true });
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.SampleOriginTraces)
        {
            var idx = _dbContext.SampleOriginTraces.FindIndex(t => t.SpecimenId == trace.SpecimenId);
            if (idx >= 0)
            {
                _dbContext.SampleOriginTraces[idx] = trace;
            }
            else
            {
                _dbContext.SampleOriginTraces.Add(trace);
            }
        }

        return await Task.FromResult(true);
    }
}
