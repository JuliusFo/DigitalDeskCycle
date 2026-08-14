using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskCycle.Core.Contracts;
using DeskCycle.Core.Data;
using DeskCycle.Core.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeskCycle.Desktop.ViewModels;

/// <summary>One row of the history list, pre-formatted.</summary>
public sealed record SessionRow(
    int Id,
    string Date,
    string Duration,
    string Revolutions,
    string Distance,
    string AverageRpm,
    string Resistance);

public sealed partial class HistoryViewModel(
    IDbContextFactory<TrackerDbContext> dbFactory,
    IOptions<TrackingOptions> options) : ObservableObject
{
    private readonly TrackingOptions _options = options.Value;

    [ObservableProperty]
    private int _days = 30;

    [ObservableProperty]
    private string _summary = "";

    public ObservableCollection<SessionRow> Sessions { get; } = [];

    /// <summary>Raw data for the daily chart; drawing happens in the view.</summary>
    public IReadOnlyList<DailyStatsDto> Daily { get; private set; } = [];

    public async Task LoadAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var since = DateTimeOffset.UtcNow.AddDays(-Days);

        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.StartedAt >= since)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync();

        Sessions.Clear();
        foreach (var session in sessions)
        {
            Sessions.Add(ToRow(SessionDto.From(session, _options.MetersPerRevolution)));
        }

        Daily = sessions
            .Where(s => s.EndedAt is not null)
            .GroupBy(s => DateOnly.FromDateTime(s.StartedAt.ToLocalTime().Date))
            .OrderBy(g => g.Key)
            .Select(g => new DailyStatsDto(
                g.Key,
                g.Count(),
                g.Sum(s => s.Revolutions),
                g.Sum(s => s.Revolutions) * _options.MetersPerRevolution,
                TimeSpan.FromTicks(g.Sum(s => (s.EndedAt!.Value - s.StartedAt).Ticks))))
            .ToList();

        var revolutions = sessions.Sum(s => s.Revolutions);
        var distanceKm = revolutions * _options.MetersPerRevolution / 1000;
        var duration = TimeSpan.FromTicks(sessions
            .Where(s => s.EndedAt is not null)
            .Sum(s => (s.EndedAt!.Value - s.StartedAt).Ticks));

        Summary = sessions.Count == 0
            ? "Keine Einheiten in diesem Zeitraum."
            : $"{sessions.Count} Einheiten · {revolutions:N0} Umdrehungen · "
              + $"{distanceKm:0.0} km · {duration.TotalHours:0.0} Stunden";
    }

    public async Task<SessionDetailViewModel> CreateDetailAsync(int sessionId)
    {
        var detail = new SessionDetailViewModel(dbFactory, _options, sessionId);
        await detail.LoadAsync();
        return detail;
    }

    private static SessionRow ToRow(SessionDto session) => new(
        session.Id,
        session.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
        session.Duration.ToString(@"hh\:mm\:ss"),
        session.Revolutions.ToString("N0"),
        session.DistanceMeters >= 1000
            ? $"{session.DistanceMeters / 1000:0.00} km"
            : $"{session.DistanceMeters:0} m",
        session.AverageRpm.ToString("0"),
        session.ResistanceLevel?.ToString() ?? "—");
}
