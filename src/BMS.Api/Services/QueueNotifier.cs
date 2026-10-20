using BMS.Api.Hubs;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Api.Services;

/// <summary>
/// Bridge implementation of IQueueNotifier using ASP.NET Core SignalR.
/// </summary>
public class QueueNotifier : IQueueNotifier
{
    private readonly IHubContext<QueueHub> _hubContext;

    public QueueNotifier(IHubContext<QueueHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyPatientCalledAsync(string roomId, object payload, CancellationToken ct = default)
    {
        await _hubContext.Clients.Group($"room_{roomId}").SendAsync("PatientCalled", payload, cancellationToken: ct);
        await _hubContext.Clients.Group("public_board").SendAsync("PatientCalled", payload, cancellationToken: ct);
        await _hubContext.Clients.All.SendAsync("PatientCalled", payload, cancellationToken: ct);
    }

    public async Task NotifyQueueUpdatedAsync(string? roomId, object payload, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(roomId))
        {
            await _hubContext.Clients.Group($"room_{roomId}").SendAsync("QueueUpdated", payload, cancellationToken: ct);
        }
        await _hubContext.Clients.Group("public_board").SendAsync("QueueUpdated", payload, cancellationToken: ct);
        await _hubContext.Clients.All.SendAsync("QueueUpdated", payload, cancellationToken: ct);
    }
}
