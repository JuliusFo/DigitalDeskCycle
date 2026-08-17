using DeskCycle.Core.Data;
using DeskCycle.Core.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeskCycle.Core.Statistics;

/// <summary>
/// Reads the stored samples of a period and feeds them into an
/// <see cref="ActivityAccumulator"/>.
///
/// The query deliberately goes through the sessions rather than straight over
/// the samples' timestamps: an index already exists for both steps
/// (<c>IX_Sessions_StartedAt</c> and <c>IX_Samples_SessionId_Timestamp</c>).
/// An additional index on the timestamp alone would otherwise have been
/// necessary, at a cost of roughly 40 percent more storage.
/// </summary>
public sealed class PeriodStatisticsLoader(
    IDbContextFactory<TrackerDbContext> dbFactory,
    IOptions<TrackingOptions> options,
    IEnergyModel energy)
{
    private readonly TrackingOptions _options = options.Value;

    public ActivityAccumulator CreateAccumulator() =>
        new(_options.MetersPerRevolution, TimeSpan.FromSeconds(_options.PauseThresholdSeconds), energy);

    public async Task<ActivityAccumulator> LoadAsync(
        DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var accumulator = CreateAccumulator();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // A session that began before the cut-off and ran past it belongs here
        // too -- only its samples from after the cut-off count.
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.StartedAt >= since || s.EndedAt == null || s.EndedAt >= since)
            .OrderBy(s => s.StartedAt)
            .Select(s => new { s.Id, s.StartedAt })
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            // Movement only. Newer recordings contain nothing else anyway; older
            // ones still hold zero values from standstill, and those have to drop
            // out here so they turn into time gaps -- and thereby into pauses, by
            // the same procedure that handles new data.
            var samples = await db.Samples.AsNoTracking()
                .Where(s => s.SessionId == session.Id && s.Timestamp >= since && s.Rpm > 0)
                .OrderBy(s => s.Timestamp)
                .Select(s => new { s.Timestamp, s.Revolutions, s.Rpm })
                .ToListAsync(cancellationToken);

            int? previousRevolutions = null;

            foreach (var sample in samples)
            {
                // The first sample of a session inside the window serves only as
                // a reference point: whatever was counted before it lies ahead of
                // the cut-off and does not belong to this period.
                var delta = previousRevolutions is null
                    ? 0
                    : sample.Revolutions - previousRevolutions.Value;

                previousRevolutions = sample.Revolutions;

                accumulator.Add(sample.Timestamp, sample.Rpm, delta, session.StartedAt);
            }
        }

        return accumulator;
    }
}
