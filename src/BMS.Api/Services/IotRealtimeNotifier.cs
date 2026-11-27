using System;
using System.Threading;
using System.Threading.Tasks;
using BMS.Api.Hubs;
using BMS.Application.DTOs.IoT;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BMS.Api.Services;

/// <summary>
/// ASP.NET Core SignalR bridge implementation of <see cref="IIotRealtimeNotifier"/>.
/// Pushes live multi-device IoT status updates and ISO 15189 alert events to all connected Web/Mobile staff clients.
/// </summary>
public sealed class IotRealtimeNotifier : IIotRealtimeNotifier
{
    private readonly IHubContext<IotHub> _hubContext;
    private readonly ILogger<IotRealtimeNotifier> _logger;

    public IotRealtimeNotifier(IHubContext<IotHub> hubContext, ILogger<IotRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<bool> BroadcastAlertAsync(string eventType, IotAlertDto alert, CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients.Group(IotHub.MonitoringGroup).SendAsync(
                "IotAlertUpdated",
                new
                {
                    eventType,
                    alert,
                    timestamp = DateTime.UtcNow
                },
                cancellationToken: ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[IoT SignalR] Failed to broadcast alert {AlertId}", alert.Id);
            return false;
        }
    }

    public async Task BroadcastTelemetryStatusAsync(IotConnectionStatusDto status, CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients.Group(IotHub.MonitoringGroup).SendAsync(
                "IotDeviceStatusUpdated",
                status,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[IoT SignalR] Failed to broadcast device status for {DeviceId}", status.DeviceId);
        }
    }
}
