using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskCycle.Core.Tracking;

/// <summary>
/// Picks the source of samples: the first one that can be used.
///
/// A running connection is not taken away mid-ride. A change of source resets
/// the reference count -- the two sources count different counters -- and the
/// revolutions in between would be lost. While a fallback is counting and no
/// session is running, however, a more preferred source that has become
/// available again does take over: after a cable is plugged back in, the USB
/// connection would otherwise stay unused until the radio link drops on its own.
///
/// Only in that direction. Whether the serial port is there costs a look at a
/// list; whether the bike is within radio reach would cost a scan alongside the
/// running connection.
/// </summary>
public sealed class CadenceSourceCoordinator(
    IEnumerable<ICadenceSource> sources,
    SessionRecorder recorder,
    ILogger<CadenceSourceCoordinator> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>How often a running fallback looks whether something better is back.</summary>
    private static readonly TimeSpan TakeoverCheckInterval = TimeSpan.FromSeconds(5);

    private readonly ICadenceSource[] _sources = sources.ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var used = false;
                var handedOver = false;

                for (var index = 0; index < _sources.Length; index++)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var source = _sources[index];

                    // Its own token so that the watcher below can end this run
                    // without taking the whole application down with it.
                    using var runCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var watch = WatchForPreferredSourceAsync(index, runCts);

                    try
                    {
                        used = await source.TryRunAsync(runCts.Token);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        used = true;    // handed over, see below
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Source {Source} failed.", source.Name);
                        used = true;    // it was there, it just broke off
                    }
                    finally
                    {
                        // Read before cancelling: afterwards every run looks
                        // like a handover.
                        handedOver = runCts.IsCancellationRequested
                            && !stoppingToken.IsCancellationRequested;

                        await runCts.CancelAsync();
                        await watch;
                    }

                    if (used)
                    {
                        break;
                    }
                }

                // Either the active source has just dropped or none was to be
                // had -- both mean nothing is counting. Reported before the
                // next round rather than after it: the search across all
                // sources takes a Bluetooth scan's worth of seconds, and until
                // then the display would keep showing the connection that has
                // just gone.
                //
                // Not after a handover: that one is planned and over within a
                // moment, and "no sensor" in between would only be a flicker.
                if (!handedOver && !stoppingToken.IsCancellationRequested)
                {
                    recorder.OnSourceUnavailable();
                }

                if (!handedOver)
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
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

    /// <summary>
    /// Watches, while a fallback is counting, whether a more preferred source
    /// has become available again -- and if so ends the current run, so that the
    /// next round picks the better one.
    /// </summary>
    private async Task WatchForPreferredSourceAsync(int currentIndex, CancellationTokenSource runCts)
    {
        if (currentIndex == 0)
        {
            return;     // nothing ranks above the first source
        }

        try
        {
            while (!runCts.IsCancellationRequested)
            {
                await Task.Delay(TakeoverCheckInterval, runCts.Token);

                // Only between sessions. The value is written from the read
                // loop, but it is a single int: at worst it is one beat out of
                // date and the handover happens five seconds later.
                if (recorder.HasActiveSession)
                {
                    continue;
                }

                for (var index = 0; index < currentIndex; index++)
                {
                    if (!_sources[index].CanTakeOver())
                    {
                        continue;
                    }

                    logger.LogInformation(
                        "{Preferred} is available again, handing over from {Current}.",
                        _sources[index].Name, _sources[currentIndex].Name);

                    await runCts.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The run has ended by itself.
        }
    }
}
