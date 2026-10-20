using BMS.Schedule.DTOs;

namespace BMS.Application.Interfaces;

public interface IScheduleService
{
    Task<List<ShiftDto>> ListAsync(string doctorId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<ShiftDto> CreateAsync(CreateShiftRequest request, CancellationToken ct = default);
    Task<ShiftDto> UpdateAsync(string id, UpdateShiftRequest request, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<ShiftDto?> GetByIdAsync(string id, CancellationToken ct = default);
}
