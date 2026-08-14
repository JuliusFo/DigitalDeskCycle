using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskCycle.Core.Tracking;

/// <summary>
/// Picks the source of samples: the first one that can be used.
///
/// Deliberately without preemption while running -- if Bluetooth is counting and
/// a USB cable is plugged in on the side, the existing connection stays. Only
/// once it drops does the search start over. Otherwise the source would change
/// mid-session without anyone having asked for it.
/// </summary>
public sealed class CadenceSourceCoordinator(
    IEnumerable<ICadenceSource> sources,
    SessionRecorder recorder,
    ILogger<CadenceSourceCoordinator> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly ICadenceSource[] _sources = sources.ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var used = false;

                foreach (var source in _sources)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        used = await source.TryRunAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Source {Source} failed.", source.Name);
                        used = true;    // it was there, it just broke off
                    }

                    if (used)
                    {
                        break;
                    }
                }

                if (!used)
                {
                    recorder.OnSourceUnavailable();
                }

                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // regular shutdown
        }
        finally
        {
            // Close a running session cleanly, rather than leaving it open.
            await recorder.CloseOpenSessionAsync(CancellationToken.None);
        }
    }
}
