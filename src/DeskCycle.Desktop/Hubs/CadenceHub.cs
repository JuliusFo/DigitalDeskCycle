using DeskCycle.Core.Tracking;
using Microsoft.AspNetCore.SignalR;

namespace DeskCycle.Desktop.Hubs;

/// <summary>
/// Live values for consumers outside this process. A pure server-to-client
/// channel; the message is called "Live" and carries a <see cref="LiveStatus"/>.
/// </summary>
public sealed class CadenceHub(LiveStatusService live) : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Send the current state straight away, so a new client does not stare at
        // an empty display until the next sample arrives.
        await Clients.Caller.SendAsync("Live", live.Current);
        await base.OnConnectedAsync();
    }
}
