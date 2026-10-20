using BMS.Application.DTOs;

namespace BMS.Application.Interfaces;

public interface ILabQueueService
{
    Task<(List<LabQueueItemDto> Items, int TotalCount)> GetQueueAsync(QueueFilterDto filter);
    Task<LabQueueItemDto?> GetSampleByIdAsync(string id);

    /// <summary>
    /// Records sample collection time, validates timestamps and patient/appointment association, and persists to MongoDB blood_samples (Record Sample Time use case).
    /// </summary>
    Task<LabQueueItemDto> RecordSampleCollectionTimeAsync(
        string? idOrBarcode,
        RecordSampleCollectionRequest request,
        string? operatorUserId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Validates permitted ISO 15189 status transitions, records lifecycle timestamps &amp; audit trail, and persists to MongoDB blood_samples (Update Sample Status use case).
    /// </summary>
    Task<LabQueueItemDto> UpdateSampleStatusDetailedAsync(
        string idOrBarcode,
        UpdateSampleStatusDto dto,
        string? operatorUserId = null,
        CancellationToken ct = default);

    Task<bool> UpdateStatusAsync(string id, UpdateSampleStatusDto dto);
    Task<bool> RequestRerunAsync(string id, SampleRerunRequestDto dto);
    Task<List<SampleRerunItemDto>> GetRerunRequestsAsync();
    Task<SampleRerunDetailsDto?> GetSampleRerunDetailsAsync(string id);
}
