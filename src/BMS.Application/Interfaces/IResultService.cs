using BMS.TestResults.DTOs;

namespace BMS.Application.Interfaces;

public interface IResultService
{
    Task<EnterResultResponse> EnterAsync(EnterResultRequest request, string technicianId, CancellationToken ct = default);
}