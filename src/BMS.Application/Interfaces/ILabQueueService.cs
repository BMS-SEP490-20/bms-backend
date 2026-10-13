using BMS.Application.DTOs;

namespace BMS.Application.Interfaces;

public interface ILabQueueService
{
    Task<(List<LabQueueItemDto> Items, int TotalCount)> GetQueueAsync(QueueFilterDto filter);
    Task<LabQueueItemDto?> GetSampleByIdAsync(string id);
    Task<bool> UpdateStatusAsync(string id, UpdateSampleStatusDto dto);
    Task<bool> RequestRerunAsync(string id, SampleRerunRequestDto dto);
}
