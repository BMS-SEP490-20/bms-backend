using BMS.Application.DTOs.Reception;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Interfaces;

/// <summary>
/// UC28 – Smart Queue & Clinical Reception Service interface.
/// </summary>
public interface IPatientReceptionService
{
    Task<QueueItemDto?> CallNextPatientAsync(CallNextPatientRequest request, CancellationToken ct = default);
    Task<QueueItemDto> RecallPatientAsync(RecallPatientRequest request, CancellationToken ct = default);
    Task<bool> SkipPatientAsync(SkipPatientRequest request, CancellationToken ct = default);
    Task<bool> CompletePatientAsync(CompletePatientRequest request, CancellationToken ct = default);
    Task<QueueStatusResponse> GetQueueStatusAsync(string? roomId, string? doctorId, CancellationToken ct = default);
}
