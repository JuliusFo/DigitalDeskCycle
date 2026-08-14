using DeskCycle.Core.Tracking;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskCycle.Desktop.Hubs;

/// <summary>
/// Forwards the live values from the core to SignalR.
///
/// Runs as a hosted service *inside* the web server, which ties the bridge to
/// its lifetime automatically: no web server, no bridge. The
/// <see cref="SessionRecorder"/> still knows only the
/// <see cref="LiveStatusService"/> and no transport at all.
/// </summary>
public sealed class SignalRLiveBridge(
    LiveStatusService live,
    IHubContext<CadenceHub> hub,
    ILogger<SignalRLiveBridge> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        live.Changed += OnLiveChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        live.Changed -= OnLiveChanged;
        return Task.CompletedTask;
    }

    private void OnLiveChanged(LiveStatus status)
    {
        // Deliberately not awaited: the measurement loop must not depend on
        // whether a slow or stuck client accepts its message.
        _ = SendAsync(status);
    }

    private async Task SendAsync(LiveStatus status)
    {
        try
        {
            await hub.Clients.All.SendAsync("Live", status);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Live value could not be sent to the SignalR clients.");
        }
    }
}
