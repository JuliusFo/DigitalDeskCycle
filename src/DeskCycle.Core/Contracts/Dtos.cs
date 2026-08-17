using DeskCycle.Core.Data;
using DeskCycle.Core.Statistics;

namespace DeskCycle.Core.Contracts;

public sealed record SessionDto(
    int Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    TimeSpan Duration,
    int Revolutions,
    double DistanceMeters,
    double AverageRpm,
    int? ResistanceLevel,
    string? Note,
    /// <summary>
    /// Estimated kilocalories. null when the caller passed no energy model or no
    /// body weight is stated -- the desktop views do not show the figure and ask
    /// for it accordingly.
    /// </summary>
    double? CaloriesKcal = null)
{
    public static SessionDto From(
        Session session, double metersPerRevolution, IEnergyModel? energy = null)
    {
        // For a session still running, EndedAt is null. Measuring against
        // StartedAt would yield a duration of 0 there -- what is meant is "up to
        // now".
        var duration = (session.EndedAt ?? DateTimeOffset.UtcNow) - session.StartedAt;
        var averageRpm = duration.TotalMinutes > 0 ? session.Revolutions / duration.TotalMinutes : 0;

        return new SessionDto(
            session.Id,
            session.StartedAt,
            session.EndedAt,
            duration,
            session.Revolutions,
            session.Revolutions * metersPerRevolution,
            averageRpm,
            session.ResistanceLevel,
            session.Note,
            energy?.Kcal(averageRpm, duration, session.ResistanceLevel));
    }
}

public sealed record SampleDto(DateTimeOffset Timestamp, int Revolutions, double Rpm);

public sealed record SessionDetailDto(SessionDto Session, IReadOnlyList<SampleDto> Samples);

public sealed record DailyStatsDto(
    DateOnly Date,
    int Sessions,
    int Revolutions,
    double DistanceMeters,
    TimeSpan Duration);
