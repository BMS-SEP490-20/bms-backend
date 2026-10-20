using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Interfaces;

/// <summary>
/// Abstraction for real-time SignalR notifications for patient queue updates.
/// </summary>
public interface IQueueNotifier
{
    Task NotifyPatientCalledAsync(string roomId, object payload, CancellationToken ct = default);
    Task NotifyQueueUpdatedAsync(string? roomId, object payload, CancellationToken ct = default);
}
