using DeskCycle.Core.Contracts;
using DeskCycle.Core.Data;
using DeskCycle.Core.Options;
using DeskCycle.Core.Statistics;
using DeskCycle.Core.Tracking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeskCycle.Desktop.Api;

/// <summary>
/// The read-only interface to the outside world. Live values additionally arrive
/// over SignalR at /hubs/cadence.
///
/// Distances are computed here from the stored revolutions and never read from
/// the database. Correcting MetersPerRevolution therefore takes effect across
/// the whole history immediately.
/// </summary>
public static class TrackingEndpoints
{
    public static IEndpointRouteBuilder MapTrackingApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/live", (LiveStatusService live) => live.Current);

        api.MapGet("/sessions", async (
            TrackerDbContext db,
            IOptions<TrackingOptions> options,
            IEnergyModel energy,
            CancellationToken ct,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int take = 50) =>
        {
            var query = db.Sessions.AsNoTracking();

            if (from is not null)
            {
                query = query.Where(s => s.StartedAt >= from);
            }

            if (to is not null)
            {
                query = query.Where(s => s.StartedAt < to);
            }

            var sessions = await query
                .OrderByDescending(s => s.StartedAt)
                .Take(Math.Clamp(take, 1, 500))
                .ToListAsync(ct);

            return sessions.Select(s => SessionDto.From(s, options.Value.MetersPerRevolution, energy));
        });

        api.MapGet("/sessions/{id:int}", async (
            int id,
            TrackerDbContext db,
            IOptions<TrackingOptions> options,
            IEnergyModel energy,
            CancellationToken ct) =>
        {
            var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
            if (session is null)
            {
                return Results.NotFound();
            }

            var samples = await db.Samples.AsNoTracking()
                .Where(s => s.SessionId == id)
                .OrderBy(s => s.Timestamp)
                .Select(s => new SampleDto(s.Timestamp, s.Revolutions, s.Rpm))
                .ToListAsync(ct);

            return Results.Ok(new SessionDetailDto(
                SessionDto.From(session, options.Value.MetersPerRevolution, energy), samples));
        });

        api.MapGet("/stats/daily", async (
            TrackerDbContext db,
            IOptions<TrackingOptions> options,
            CancellationToken ct,
            int days = 30) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 366));

            var sessions = await db.Sessions.AsNoTracking()
                .Where(s => s.StartedAt >= since && s.EndedAt != null)
                .ToListAsync(ct);

            var metersPerRevolution = options.Value.MetersPerRevolution;

            return sessions
                .GroupBy(s => DateOnly.FromDateTime(s.StartedAt.ToLocalTime().Date))
                .OrderBy(g => g.Key)
                .Select(g => new DailyStatsDto(
                    g.Key,
                    g.Count(),
                    g.Sum(s => s.Revolutions),
                    g.Sum(s => s.Revolutions) * metersPerRevolution,
                    TimeSpan.FromTicks(g.Sum(s => (s.EndedAt!.Value - s.StartedAt).Ticks))))
                .ToList();
        });

        return app;
    }
}
