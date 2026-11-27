using System.Threading.Tasks;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BMS.Api.Hubs;

/// <summary>
/// Real-time SignalR Hub for multi-device IoT Cold-Chain monitoring and ISO 15189 threshold alerts.
/// </summary>
[Authorize(Roles = RoleSets.AdminManagerLab)]
public class IotHub : Hub
{
    public const string MonitoringGroup = "iot_monitors";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, MonitoringGroup);
        await base.OnConnectedAsync();
    }

    public async Task JoinIotMonitoring()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, MonitoringGroup);
    }

    public async Task LeaveIotMonitoring()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, MonitoringGroup);
    }
}
