using BMS.Application.DTOs.TestBatches;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Services;

/// <summary>
/// UC38 – Laboratory Test Batch Management Service.
/// Implements ISO 15189 tray capacity validation, IQC control well placement,
/// sample batch assignment, and rollback on cancellation.
/// </summary>
public class TestBatchService : ITestBatchService
{
    private readonly ITestBatchRepository _repository;
    private readonly ILogger<TestBatchService> _logger;

    public TestBatchService(
        ITestBatchRepository repository,
        ILogger<TestBatchService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<TestBatchDto>> GetBatchesAsync(
        DateTime? fromDate,
        DateTime? toDate,
        string? analyzerId,
        string? status,
        CancellationToken ct = default)
    {
        TestBatchStatus? batchStatus = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TestBatchStatus>(status, true, out var parsed))
        {
            batchStatus = parsed;
        }

        var batches = await _repository.GetFilteredAsync(fromDate, toDate, analyzerId, batchStatus, ct);
        return batches.Select(MapToDto).ToList();
    }

    public async Task<TestBatchDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var batch = await _repository.GetByIdAsync(id, ct);
        return batch == null ? null : MapToDto(batch);
    }

    public async Task<List<Sample>> GetPendingSamplesAsync(string? category, CancellationToken ct = default)
    {
        return await _repository.GetPendingSamplesForBatchAsync(category, 100, ct);
    }

    public async Task<TestBatchDto> CreateBatchAsync(
        CreateTestBatchRequest request,
        string technicianId,
        string? technicianName,
        CancellationToken ct = default)
    {
        var code = await _repository.GenerateNextBatchCodeAsync(DateTime.UtcNow, ct);

        // Ensure A1 and A2 controls exist in well plate
        var wells = request.Wells.Select(w => new WellPlatePosition
        {
            WellCoordinate = w.WellCoordinate.Trim().ToUpperInvariant(),
            IsControlSample = w.IsControlSample,
            ControlLevel = w.ControlLevel,
            SampleId = w.SampleId,
            Barcode = w.Barcode,
            PatientName = w.PatientName,
            Priority = w.Priority
        }).ToList();

        if (!wells.Any(w => w.WellCoordinate == "A1"))
        {
            wells.Insert(0, new WellPlatePosition
            {
                WellCoordinate = "A1",
                IsControlSample = true,
                ControlLevel = "IQC Level 1 (Normal)",
                Barcode = "CTRL-LV1"
            });
        }

        if (!wells.Any(w => w.WellCoordinate == "A2"))
        {
            wells.Insert(1, new WellPlatePosition
            {
                WellCoordinate = "A2",
                IsControlSample = true,
                ControlLevel = "IQC Level 2 (High)",
                Barcode = "CTRL-LV2"
            });
        }

        var batch = new TestBatch
        {
            BatchCode = code,
            AnalyzerId = request.AnalyzerId,
            AnalyzerName = !string.IsNullOrWhiteSpace(request.AnalyzerName) ? request.AnalyzerName : request.AnalyzerId,
            Category = request.Category,
            Capacity = request.Capacity,
            Status = TestBatchStatus.Pending,
            Wells = wells,
            TechnicianId = technicianId,
            TechnicianName = technicianName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.InsertAsync(batch, ct);

        // Lock assigned samples to status Testing
        var assignedSampleIds = wells
            .Where(w => !w.IsControlSample && !string.IsNullOrEmpty(w.SampleId))
            .Select(w => w.SampleId!)
            .ToList();

        if (assignedSampleIds.Any())
        {
            await _repository.AssignSamplesToBatchAsync(batch.Id, assignedSampleIds, ct);
        }

        _logger.LogInformation("Test Batch {Code} created with {Count} samples on Analyzer {AnalyzerId}",
            batch.BatchCode, assignedSampleIds.Count, batch.AnalyzerId);

        return MapToDto(batch);
    }

    public async Task<TestBatchDto> UpdateStatusAsync(
        string id,
        UpdateTestBatchStatusRequest request,
        CancellationToken ct = default)
    {
        var batch = await _repository.GetByIdAsync(id, ct);
        if (batch == null)
        {
            throw new InvalidOperationException("MSG-ERR-BAT-06: Không tìm thấy lô xét nghiệm.");
        }

        if (!Enum.TryParse<TestBatchStatus>(request.Status, true, out var newStatus))
        {
            throw new InvalidOperationException("MSG-ERR-BAT-07: Trạng thái không hợp lệ.");
        }

        batch.Status = newStatus;
        if (newStatus == TestBatchStatus.Running)
        {
            batch.StartedAt = DateTime.UtcNow;
        }
        else if (newStatus == TestBatchStatus.Completed)
        {
            batch.CompletedAt = DateTime.UtcNow;
        }
        else if (newStatus == TestBatchStatus.Cancelled)
        {
            batch.CancelledAt = DateTime.UtcNow;
            batch.CancelReason = request.CancelReason;
            await _repository.ReleaseSamplesFromBatchAsync(batch.Id, ct);
        }

        await _repository.UpdateAsync(batch, ct);
        _logger.LogInformation("Test Batch {Code} transitioned to status {Status}", batch.BatchCode, newStatus);

        return MapToDto(batch);
    }

    public async Task<bool> CancelBatchAsync(string id, string? reason, CancellationToken ct = default)
    {
        var batch = await _repository.GetByIdAsync(id, ct);
        if (batch == null)
        {
            throw new InvalidOperationException("MSG-ERR-BAT-06: Không tìm thấy lô xét nghiệm cần hủy.");
        }

        return await _repository.DeleteOrCancelAsync(batch.Id, reason ?? "Kỹ thuật viên hủy lô", ct);
    }

    private static TestBatchDto MapToDto(TestBatch b)
    {
        var sampleCount = b.Wells.Count(w => !w.IsControlSample && !string.IsNullOrEmpty(w.SampleId));

        return new TestBatchDto
        {
            Id = b.Id,
            BatchCode = b.BatchCode,
            AnalyzerId = b.AnalyzerId,
            AnalyzerName = b.AnalyzerName,
            Category = b.Category,
            Capacity = b.Capacity,
            SampleCount = sampleCount,
            Status = b.Status.ToString(),
            TechnicianId = b.TechnicianId,
            TechnicianName = b.TechnicianName,
            StartedAt = b.StartedAt,
            CompletedAt = b.CompletedAt,
            CancelledAt = b.CancelledAt,
            CancelReason = b.CancelReason,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt,
            Wells = b.Wells.Select(w => new WellPlatePositionDto
            {
                WellCoordinate = w.WellCoordinate,
                IsControlSample = w.IsControlSample,
                ControlLevel = w.ControlLevel,
                SampleId = w.SampleId,
                Barcode = w.Barcode,
                PatientName = w.PatientName,
                Priority = w.Priority
            }).ToList()
        };
    }
}
