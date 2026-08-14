using CommunityToolkit.Mvvm.ComponentModel;
using DeskCycle.Core.Contracts;
using DeskCycle.Core.Data;
using DeskCycle.Core.Options;
using Microsoft.EntityFrameworkCore;

namespace DeskCycle.Desktop.ViewModels;

public sealed partial class SessionDetailViewModel(
    IDbContextFactory<TrackerDbContext> dbFactory,
    TrackingOptions options,
    int sessionId) : ObservableObject
{
    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _summary = "";

    /// <summary>
    /// The resistance level is invisible in the signal -- the adjustment on the
    /// bike is purely mechanical. Hence it can be filled in by hand; it is the
    /// basis for a future power estimate.
    /// </summary>
    [ObservableProperty]
    private string _resistanceLevel = "";

    [ObservableProperty]
    private string _note = "";

    [ObservableProperty]
    private string _status = "";

    public IReadOnlyList<SampleDto> Samples { get; private set; } = [];

    public async Task LoadAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null)
        {
            Title = "Einheit nicht gefunden";
            return;
        }

        Samples = await db.Samples.AsNoTracking()
            .Where(s => s.SessionId == sessionId)
            .OrderBy(s => s.Timestamp)
            .Select(s => new SampleDto(s.Timestamp, s.Revolutions, s.Rpm))
            .ToListAsync();

        var dto = SessionDto.From(session, options.MetersPerRevolution);

        Title = dto.StartedAt.ToLocalTime().ToString("dddd, dd.MM.yyyy 'um' HH:mm");
        Summary = $"{dto.Duration:hh\\:mm\\:ss} · {dto.Revolutions:N0} Umdrehungen · "
                  + $"{dto.DistanceMeters / 1000:0.00} km · Ø {dto.AverageRpm:0} U/min";
        ResistanceLevel = dto.ResistanceLevel?.ToString() ?? "";
        Note = dto.Note ?? "";
    }

    public async Task SaveAsync()
    {
        int? level = null;

        if (!string.IsNullOrWhiteSpace(ResistanceLevel))
        {
            if (!int.TryParse(ResistanceLevel, out var parsed))
            {
                Status = "Die Widerstandsstufe muss eine ganze Zahl sein.";
                return;
            }

            level = parsed;
        }

        var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();

        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Sessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ResistanceLevel, level)
                .SetProperty(x => x.Note, note));

        Status = "Gespeichert.";
    }
}
