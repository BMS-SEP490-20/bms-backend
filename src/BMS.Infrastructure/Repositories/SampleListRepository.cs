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
/// Repository implementation for sample collection lists and phlebotomy batches (UC37).
/// Integrates MongoDB collection 'sample_collection_lists' with in-memory synchronization.
/// </summary>
public class SampleListRepository : ISampleListRepository
{
    private readonly IMongoCollection<SampleCollectionList>? _collection;
    private readonly BmsDbContext _dbContext;

    public SampleListRepository(IMongoDatabase? database, BmsDbContext dbContext)
    {
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _collection = database.GetCollection<SampleCollectionList>("sample_collection_lists");
            }
            catch
            {
                _collection = null;
            }
        }
    }

    public async Task<SampleCollectionList?> GetByIdAsync(string id)
    {
        if (_collection != null)
        {
            try
            {
                var doc = await _collection.Find(l => l.Id == id).FirstOrDefaultAsync();
                if (doc != null) return doc;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.SampleCollectionLists)
        {
            return _dbContext.SampleCollectionLists.FirstOrDefault(l => l.Id == id);
        }
    }

    public async Task<SampleCollectionList?> GetByBatchCodeAsync(string batchCode)
    {
        if (_collection != null)
        {
            try
            {
                var doc = await _collection.Find(l => l.BatchCode == batchCode).FirstOrDefaultAsync();
                if (doc != null) return doc;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.SampleCollectionLists)
        {
            return _dbContext.SampleCollectionLists.FirstOrDefault(l => l.BatchCode.Equals(batchCode, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<IEnumerable<SampleCollectionList>> GetAllAsync(DateTime? date = null, SampleCollectionListStatus? status = null)
    {
        if (_collection != null)
        {
            try
            {
                var builder = Builders<SampleCollectionList>.Filter;
                var filter = builder.Empty;

                if (date.HasValue)
                {
                    var targetDate = date.Value.Date;
                    filter &= builder.Gte(l => l.ScheduledDate, targetDate) & builder.Lt(l => l.ScheduledDate, targetDate.AddDays(1));
                }

                if (status.HasValue)
                {
                    filter &= builder.Eq(l => l.Status, status.Value);
                }

                var list = await _collection.Find(filter).SortByDescending(l => l.CreatedAt).ToListAsync();
                if (list != null && list.Count > 0) return list;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.SampleCollectionLists)
        {
            var query = _dbContext.SampleCollectionLists.AsQueryable();

            if (date.HasValue)
            {
                var targetDate = date.Value.Date;
                query = query.Where(l => l.ScheduledDate.Date == targetDate);
            }

            if (status.HasValue)
            {
                query = query.Where(l => l.Status == status.Value);
            }

            return query.OrderByDescending(l => l.CreatedAt).ToList();
        }
    }

    public async Task<SampleCollectionList> CreateAsync(SampleCollectionList list)
    {
        if (string.IsNullOrEmpty(list.BatchCode))
        {
            list.BatchCode = await GenerateNextBatchCodeAsync();
        }

        list.CreatedAt = DateTime.UtcNow;

        if (_collection != null)
        {
            try
            {
                await _collection.InsertOneAsync(list);
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.SampleCollectionLists)
        {
            _dbContext.SampleCollectionLists.Add(list);
        }

        return list;
    }

    public async Task<bool> UpdateAsync(SampleCollectionList list)
    {
        if (_collection != null)
        {
            try
            {
                var result = await _collection.ReplaceOneAsync(l => l.Id == list.Id, list);
                if (result.IsAcknowledged && result.ModifiedCount > 0)
                {
                    lock (_dbContext.SampleCollectionLists)
                    {
                        var idx = _dbContext.SampleCollectionLists.FindIndex(l => l.Id == list.Id);
                        if (idx >= 0) _dbContext.SampleCollectionLists[idx] = list;
                    }
                    return true;
                }
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.SampleCollectionLists)
        {
            var idx = _dbContext.SampleCollectionLists.FindIndex(l => l.Id == list.Id);
            if (idx >= 0)
            {
                _dbContext.SampleCollectionLists[idx] = list;
                return true;
            }
        }

        return false;
    }

    public Task<string> GenerateNextBatchCodeAsync()
    {
        var datePrefix = DateTime.UtcNow.ToString("yyMMdd");
        int count;
        lock (_dbContext.SampleCollectionLists)
        {
            count = _dbContext.SampleCollectionLists.Count(l => l.BatchCode.StartsWith($"BATCH-{datePrefix}")) + 1;
        }

        return Task.FromResult($"BATCH-{datePrefix}-A{count}");
    }
}
