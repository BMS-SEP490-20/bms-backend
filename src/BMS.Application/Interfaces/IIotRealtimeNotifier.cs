using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.IoT;

namespace BMS.Application.Interfaces;

/// <summary>
/// Real-time SignalR push contract for IoT cold-chain alerts and multi-device telemetry status updates.
/// </summary>
public interface IIotRealtimeNotifier
{
    Task<bool> BroadcastAlertAsync(string eventType, IotAlertDto alert, CancellationToken ct = default);
    Task BroadcastTelemetryStatusAsync(IotConnectionStatusDto status, CancellationToken ct = default);
}
