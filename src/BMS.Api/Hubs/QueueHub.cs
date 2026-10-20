using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace BMS.Api.Hubs;

/// <summary>
/// UC28 – Real-time SignalR Hub for clinic queues and public kiosk boards.
/// </summary>
public class QueueHub : Hub
{
    public async Task JoinRoom(string roomId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{roomId}");
    }

    public async Task LeaveRoom(string roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room_{roomId}");
    }

    public async Task JoinPublicBoard()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "public_board");
    }
}
