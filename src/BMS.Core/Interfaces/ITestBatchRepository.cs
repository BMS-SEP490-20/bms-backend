using BMS.Core.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for laboratory analyzer test batches.
/// </summary>
public interface ITestBatchRepository
{
    Task<List<TestBatch>> GetFilteredAsync(DateTime? fromDate, DateTime? toDate, string? analyzerId, TestBatchStatus? status, CancellationToken ct = default);
    Task<TestBatch?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<string> GenerateNextBatchCodeAsync(DateTime date, CancellationToken ct = default);
    Task InsertAsync(TestBatch batch, CancellationToken ct = default);
    Task UpdateAsync(TestBatch batch, CancellationToken ct = default);
    Task<bool> DeleteOrCancelAsync(string id, string cancelReason, CancellationToken ct = default);

    Task<List<Sample>> GetPendingSamplesForBatchAsync(string? category, int limit = 100, CancellationToken ct = default);
    Task AssignSamplesToBatchAsync(string batchId, IEnumerable<string> sampleIds, CancellationToken ct = default);
    Task ReleaseSamplesFromBatchAsync(string batchId, CancellationToken ct = default);
}
