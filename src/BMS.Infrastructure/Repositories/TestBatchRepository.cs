using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB repository for TestBatch and sample rack allocations.
/// </summary>
public class TestBatchRepository : ITestBatchRepository
{
    private readonly IMongoCollection<TestBatch> _collection;
    private readonly IMongoCollection<Sample> _sampleCollection;

    public TestBatchRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<TestBatch>("test_batches");
        _sampleCollection = database.GetCollection<Sample>("blood_samples");

        var indexKeys = Builders<TestBatch>.IndexKeys.Ascending(x => x.BatchCode);
        _collection.Indexes.CreateOne(new CreateIndexModel<TestBatch>(indexKeys, new CreateIndexOptions { Unique = true }));
    }

    public async Task<List<TestBatch>> GetFilteredAsync(
        DateTime? fromDate,
        DateTime? toDate,
        string? analyzerId,
        TestBatchStatus? status,
        CancellationToken ct = default)
    {
        var builder = Builders<TestBatch>.Filter;
        var filter = builder.Empty;

        if (status.HasValue)
        {
            filter &= builder.Eq(x => x.Status, status.Value);
        }

        if (!string.IsNullOrWhiteSpace(analyzerId))
        {
            filter &= builder.Eq(x => x.AnalyzerId, analyzerId);
        }

        if (fromDate.HasValue)
        {
            filter &= builder.Gte(x => x.CreatedAt, fromDate.Value.Date);
        }

        if (toDate.HasValue)
        {
            filter &= builder.Lte(x => x.CreatedAt, toDate.Value.Date.AddDays(1).AddTicks(-1));
        }

        return await _collection
            .Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<TestBatch?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.Id == id || x.BatchCode == id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string> GenerateNextBatchCodeAsync(DateTime date, CancellationToken ct = default)
    {
        var prefix = $"BAT-{date:yyMMdd}-";
        var last = await _collection
            .Find(x => x.BatchCode.StartsWith(prefix))
            .SortByDescending(x => x.BatchCode)
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        var nextSeq = 1;
        if (last != null && last.BatchCode.Length >= prefix.Length + 4)
        {
            var tail = last.BatchCode[prefix.Length..];
            if (int.TryParse(tail, out var seq))
            {
                nextSeq = seq + 1;
            }
        }

        return $"{prefix}{nextSeq:D4}";
    }

    public async Task InsertAsync(TestBatch batch, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(batch, cancellationToken: ct);
    }

    public async Task UpdateAsync(TestBatch batch, CancellationToken ct = default)
    {
        batch.UpdatedAt = DateTime.UtcNow;
        await _collection.ReplaceOneAsync(x => x.Id == batch.Id, batch, cancellationToken: ct);
    }

    public async Task<bool> DeleteOrCancelAsync(string id, string cancelReason, CancellationToken ct = default)
    {
        var batch = await GetByIdAsync(id, ct);
        if (batch == null) return false;

        var update = Builders<TestBatch>.Update
            .Set(x => x.Status, TestBatchStatus.Cancelled)
            .Set(x => x.CancelledAt, DateTime.UtcNow)
            .Set(x => x.CancelReason, cancelReason)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(x => x.Id == batch.Id, update, cancellationToken: ct);

        // Rollback invariant: Release all assigned samples back to Received status
        await ReleaseSamplesFromBatchAsync(batch.Id, ct);

        return result.ModifiedCount > 0;
    }

    public async Task<List<Sample>> GetPendingSamplesForBatchAsync(string? category, int limit = 100, CancellationToken ct = default)
    {
        var builder = Builders<Sample>.Filter;
        var filter = builder.Eq(x => x.StatusString, SampleStatus.Received.ToString()) &
                     (builder.Eq(x => x.BatchId, null) | builder.Eq(x => x.BatchId, ""));

        return await _sampleCollection
            .Find(filter)
            .SortByDescending(x => x.PriorityString) // STAT first
            .ThenBy(x => x.ReceivedAt)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task AssignSamplesToBatchAsync(string batchId, IEnumerable<string> sampleIds, CancellationToken ct = default)
    {
        var ids = sampleIds.ToList();
        if (ids.Count == 0) return;

        var filter = Builders<Sample>.Filter.In(x => x.Id, ids);
        var update = Builders<Sample>.Update
            .Set(x => x.BatchId, batchId)
            .Set(x => x.StatusString, SampleStatus.Testing.ToString())
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        await _sampleCollection.UpdateManyAsync(filter, update, cancellationToken: ct);
    }

    public async Task ReleaseSamplesFromBatchAsync(string batchId, CancellationToken ct = default)
    {
        var filter = Builders<Sample>.Filter.Eq(x => x.BatchId, batchId);
        var update = Builders<Sample>.Update
            .Set(x => x.BatchId, (string?)null)
            .Set(x => x.StatusString, SampleStatus.Received.ToString())
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        await _sampleCollection.UpdateManyAsync(filter, update, cancellationToken: ct);
    }
}
