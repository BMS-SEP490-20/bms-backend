using BMS.Application.DTOs.TestBatches;
using BMS.Core.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Interfaces;

/// <summary>
/// UC38 – Laboratory Analyzer Test Batch Service interface.
/// </summary>
public interface ITestBatchService
{
    Task<List<TestBatchDto>> GetBatchesAsync(DateTime? fromDate, DateTime? toDate, string? analyzerId, string? status, CancellationToken ct = default);
    Task<TestBatchDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<Sample>> GetPendingSamplesAsync(string? category, CancellationToken ct = default);
    Task<TestBatchDto> CreateBatchAsync(CreateTestBatchRequest request, string technicianId, string? technicianName, CancellationToken ct = default);
    Task<TestBatchDto> UpdateStatusAsync(string id, UpdateTestBatchStatusRequest request, CancellationToken ct = default);
    Task<bool> CancelBatchAsync(string id, string? reason, CancellationToken ct = default);
}
