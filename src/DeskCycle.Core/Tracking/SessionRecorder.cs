using DeskCycle.Core.Data;
using DeskCycle.Core.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeskCycle.Core.Tracking;

/// <summary>
/// Detects training sessions in the incoming stream of samples and stores them.
///
/// A session begins with the first revolution and ends once nothing happens for
/// longer than <see cref="TrackingOptions.SessionIdleTimeoutSeconds"/>. A desk
/// bike is ridden in bursts across the day -- starting and stopping by hand
/// would only be a source of mistakes.
///
/// Not thread safe: calls come exclusively from the read loop of the active
/// <see cref="ICadenceSource"/>, that is from exactly one thread.
/// </summary>
public sealed class SessionRecorder(
    IServiceScopeFactory scopeFactory,
    IOptions<TrackingOptions> options,
    LiveStatusService live,
    TimeProvider clock,
    ILogger<SessionRecorder> logger)
{
    private readonly TrackingOptions _options = options.Value;

    private int? _lastCount;
    private int _sessionId;
    private int _sessionRevolutions;
    private DateTimeOffset _sessionStartedAt;
    private DateTimeOffset _lastMovementAt;

    private bool HasActiveSession => _sessionId != 0;

    /// <summary>
    /// After (re)connecting it is unknown how far the Pico's counter has run in
    /// the meantime. The next sample therefore serves only as a reference point
    /// -- better to lose a few revolutions than to credit an invented
    /// difference. Applies to every source alike.
    /// </summary>
    public void OnSourceConnected() => _lastCount = null;

    public void OnSourceUnavailable() =>
        Publish(reading: null, sourceName: null, connected: false, clock.GetUtcNow());

    public async Task OnReadingAsync(CadenceReading reading, string sourceName, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var delta = ConsumeDelta(reading.Count);

        if (delta > 0)
        {
            _lastMovementAt = now;

            if (!HasActiveSession)
            {
                await StartSessionAsync(now, ct);
            }

            _sessionRevolutions += delta;
            await UpdateSessionRevolutionsAsync(ct);
        }

        if (HasActiveSession)
        {
            // Only movement is recorded. Seconds spent standing still made up two
            // thirds of the samples and added nothing the session itself does not
            // already record. Pauses fall out of the gaps between timestamps when
            // reading the data back.
            if (reading.Rpm > 0)
            {
                await AppendSampleAsync(now, reading.Rpm, ct);
            }

            if (now - _lastMovementAt > TimeSpan.FromSeconds(_options.SessionIdleTimeoutSeconds))
            {
                await EndSessionAsync(ct);
            }
        }

        Publish(reading, sourceName, connected: true, now, delta);
    }

    public async Task CloseOpenSessionAsync(CancellationToken ct)
    {
        if (HasActiveSession)
        {
            await EndSessionAsync(ct);
        }
    }

    /// <summary>Revolutions since the previous sample, robust against a Pico restart.</summary>
    private int ConsumeDelta(int count)
    {
        var previous = _lastCount;
        _lastCount = count;

        if (previous is null)
        {
            return 0;
        }

        if (count < previous)
        {
            // After a restart the Pico's counter begins at 0 again.
            logger.LogInformation(
                "Sensor counter reset ({Previous} -> {Count}), the Pico probably restarted.",
                previous, count);
            return count;
        }

        return count - previous.Value;
    }

    private async Task StartSessionAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();

        var session = new Session { StartedAt = now };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        _sessionId = session.Id;
        _sessionRevolutions = 0;
        _sessionStartedAt = now;

        logger.LogInformation("Session {SessionId} started.", _sessionId);
    }

    private async Task AppendSampleAsync(DateTimeOffset now, double rpm, CancellationToken ct)
    {
        var sessionId = _sessionId;
        var revolutions = _sessionRevolutions;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();

        db.Samples.Add(new SessionSample
        {
            SessionId = sessionId,
            Timestamp = now,
            Revolutions = revolutions,
            Rpm = rpm,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Redundant with the samples, but the overview list should not have to
    /// aggregate over thousands of samples for every row.
    /// </summary>
    private async Task UpdateSessionRevolutionsAsync(CancellationToken ct)
    {
        var sessionId = _sessionId;
        var revolutions = _sessionRevolutions;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();

        await db.Sessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revolutions, revolutions), ct);
    }

    private async Task EndSessionAsync(CancellationToken ct)
    {
        var sessionId = _sessionId;
        var revolutions = _sessionRevolutions;

        // The end is the time of the last revolution, not the moment the idle
        // timeout expired -- otherwise every session carries the idle time along.
        var endedAt = _lastMovementAt;

        _sessionId = 0;
        _sessionRevolutions = 0;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();

        if (revolutions < _options.MinimumSessionRevolutions)
        {
            await db.Sessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync(ct);
            logger.LogInformation(
                "Session {SessionId} discarded, only {Revolutions} revolutions.", sessionId, revolutions);
            return;
        }

        await db.Sessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.EndedAt, endedAt)
                .SetProperty(x => x.Revolutions, revolutions), ct);

        logger.LogInformation(
            "Session {SessionId} ended: {Revolutions} revolutions.", sessionId, revolutions);
    }

    /// <summary>
    /// Publishes the state through <see cref="LiveStatusService"/> and nothing
    /// else. Whoever wants to forward the values -- to SignalR, say -- subscribes
    /// there; the core knows neither ASP.NET nor any transport.
    /// </summary>
    private void Publish(
        CadenceReading? reading,
        string? sourceName,
        bool connected,
        DateTimeOffset now,
        int revolutionDelta = 0)
    {
        var rpm = reading?.Rpm ?? 0;

        var status = new LiveStatus
        {
            SensorConnected = connected,
            SourceName = sourceName,
            SessionActive = HasActiveSession,
            SessionStartedAt = HasActiveSession ? _sessionStartedAt : null,
            SessionDuration = HasActiveSession ? now - _sessionStartedAt : TimeSpan.Zero,
            SessionRevolutions = _sessionRevolutions,
            RevolutionDelta = revolutionDelta,
            Rpm = rpm,
            SpeedKmh = rpm * _options.MetersPerRevolution * 60 / 1000,
            DistanceMeters = _sessionRevolutions * _options.MetersPerRevolution,
            SuspectCount = reading?.Suspect,
        };

        live.Publish(status);
    }
}
