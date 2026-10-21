using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for Specimen History persistence and audit trail (UC70 / BR-212).
/// </summary>
public interface ISpecimenHistoryRepository
{
    Task<(List<SpecimenHistoryRecord> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? keyword = null,
        string? sampleType = null,
        string? status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken ct = default);

    Task<SpecimenHistoryRecord?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);

    Task<SpecimenHistoryRecord?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<List<SpecimenHistoryRecord>> GetByPatientIdAsync(string patientId, CancellationToken ct = default);

    Task<SpecimenHistoryRecord> CreateAsync(SpecimenHistoryRecord record, CancellationToken ct = default);

    Task<SpecimenHistoryRecord?> UpdateAsync(SpecimenHistoryRecord record, CancellationToken ct = default);

    Task<bool> AddTimelineEventAsync(string barcodeOrId, SpecimenTimelineEvent eventItem, CancellationToken ct = default);

    Task<bool> UpdateStorageAsync(string barcodeOrId, SpecimenStorageInfo storageInfo, CancellationToken ct = default);

    Task EnsureDefaultSpecimensSeededAsync(CancellationToken ct = default);
}
