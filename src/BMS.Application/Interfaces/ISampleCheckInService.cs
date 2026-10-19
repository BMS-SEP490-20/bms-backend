using BMS.Application.DTOs.Roles;
using BMS.SampleCheckIn.DTOs;

namespace BMS.Application.Interfaces;

public interface ISampleCheckInService
{
    Task<SampleDto> CheckInAsync(CheckInSampleRequest request, string technicianId, CancellationToken ct = default);
    Task<SampleDto?> LookupAsync(string code, CancellationToken ct = default);
    Task<List<SampleDto>> ListPendingAsync(CancellationToken ct = default);
}
