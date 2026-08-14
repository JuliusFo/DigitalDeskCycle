using DeskCycle.Core.Data;

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
    string? Note)
{
    public static SessionDto From(Session session, double metersPerRevolution)
    {
        // For a session still running, EndedAt is null. Measuring against
        // StartedAt would yield a duration of 0 there -- what is meant is "up to
        // now".
        var duration = (session.EndedAt ?? DateTimeOffset.UtcNow) - session.StartedAt;

        return new SessionDto(
            session.Id,
            session.StartedAt,
            session.EndedAt,
            duration,
            session.Revolutions,
            session.Revolutions * metersPerRevolution,
            duration.TotalMinutes > 0 ? session.Revolutions / duration.TotalMinutes : 0,
            session.ResistanceLevel,
            session.Note);
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
