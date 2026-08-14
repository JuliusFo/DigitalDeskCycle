using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskCycle.Core.Options;
using DeskCycle.Core.Statistics;
using DeskCycle.Core.Tracking;
using DeskCycle.Desktop.Services;
using Microsoft.Extensions.Options;

namespace DeskCycle.Desktop.ViewModels;

/// <summary>
/// The live view summarises the period since the last reset.
///
/// The figures are loaded from the database once and then carried forward with
/// every incoming sample -- the same arithmetic in both cases, because both feed
/// the same <see cref="ActivityAccumulator"/>. Re-aggregating every second would
/// be nonsense across tens of thousands of rows.
/// </summary>
public sealed partial class LiveViewModel : ObservableObject, IDisposable
{
    private readonly LiveStatusService _live;
    private readonly PeriodStatisticsLoader _loader;
    private readonly UserSettingsStore _settings;
    private readonly TrackingOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dispatcher _dispatcher;

    private ActivityAccumulator _period;
    private DateOnly? _promptDismissedOn;

    public LiveViewModel(
        LiveStatusService live,
        PeriodStatisticsLoader loader,
        UserSettingsStore settings,
        IOptions<TrackingOptions> options,
        TimeProvider clock)
    {
        _live = live;
        _loader = loader;
        _settings = settings;
        _options = options.Value;
        _clock = clock;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _period = loader.CreateAccumulator();
        _status = live.Current;
        _summary = _period.Summarize();

        live.Changed += OnLiveChanged;
    }

    /// <summary>Raised when the chart needs redrawing.</summary>
    public event Action? SeriesChanged;

    [ObservableProperty]
    private LiveStatus _status;

    [ObservableProperty]
    private PeriodSummary _summary;

    /// <summary>The day-change bar. It asks rather than deciding on its own.</summary>
    [ObservableProperty]
    private bool _showNewDayPrompt;

    public IReadOnlyList<ActivityPoint> Points => _period.Points;

    public IReadOnlyList<double> PauseMarkers => _period.PauseMarkers;

    public double SpeedGaugeMaxKmh => _options.SpeedGaugeMaxKmh;

    public async Task LoadAsync()
    {
        var since = EnsureResetAt();
        _period = await _loader.LoadAsync(since);

        Summary = _period.Summarize();
        OnPropertyChanged(string.Empty);
        SeriesChanged?.Invoke();
    }

    // ------------------------------------------------------------ display

    public bool IsSensorConnected => Status.SensorConnected;

    public string ConnectionText => Status.SensorConnected
        ? $"Sensor verbunden{(Status.SourceName is null ? "" : $" · {Status.SourceName}")}"
        : "Kein Sensor — hängt der Pico am Strom?";

    public string PeriodText
    {
        get
        {
            var since = _settings.Current.ResetAt?.ToLocalTime();
            if (since is null)
            {
                return "seit heute";
            }

            return since.Value.Date == _clock.GetLocalNow().Date
                ? $"seit heute {since.Value:HH:mm}"
                : $"seit {since.Value:dd.MM. HH:mm}";
        }
    }

    public string SpeedText => $"{Status.SpeedKmh:0.0}";

    public double GaugeFraction =>
        Math.Clamp(Status.SpeedKmh / Math.Max(1, _options.SpeedGaugeMaxKmh), 0, 1);

    public string GaugeMaxText => $"{_options.SpeedGaugeMaxKmh:0} km/h";

    public string CadenceText => $"{Status.Rpm:0}";

    public string DistanceText => Summary.DistanceMeters >= 1000
        ? $"{Summary.DistanceMeters / 1000:0.00}"
        : $"{Summary.DistanceMeters:0}";

    public string DistanceUnit => Summary.DistanceMeters >= 1000 ? "km" : "m";

    public string ActiveDurationText => Summary.ActiveDuration >= TimeSpan.FromHours(1)
        ? Summary.ActiveDuration.ToString(@"h\:mm\:ss")
        : Summary.ActiveDuration.ToString(@"mm\:ss");

    public string RevolutionsText => Summary.Revolutions.ToString("N0");

    public string AverageSpeedText => $"{Summary.AverageSpeedKmh:0.0}";

    public string AverageRpmText => $"{Summary.AverageRpm:0}";

    public string PeakSpeedText => $"{Summary.PeakSpeedKmh:0.0}";

    public string SessionsAndPausesText => $"{Summary.Sessions} · {Summary.Pauses}";

    /// <summary>
    /// The counter comes raw from the firmware. If it is not 0, the reed switch
    /// is double-counting revolutions and every value above is too high. Over
    /// Bluetooth it is null -- the profile does not know it.
    /// </summary>
    public bool HasWarning => Status.SuspectCount > 0;

    public string WarningText =>
        $"Der Sensor meldet {Status.SuspectCount} unplausible Impulse. "
        + "Verkabelung und Entprellung prüfen (siehe firmware/README.md).";

    // ------------------------------------------------------------ commands

    /// <summary>Resets to now -- a deliberate fresh start in the middle of the day.</summary>
    [RelayCommand]
    private async Task ResetNowAsync() => await ApplyResetAsync(_clock.GetUtcNow());

    /// <summary>
    /// Resets to today at 00:00 rather than to now: whoever notices the bar only
    /// after half an hour of riding should keep that half hour.
    /// </summary>
    [RelayCommand]
    private async Task ResetToTodayAsync() =>
        await ApplyResetAsync(new DateTimeOffset(_clock.GetLocalNow().Date, _clock.GetLocalNow().Offset));

    [RelayCommand]
    private void KeepCounting()
    {
        _promptDismissedOn = DateOnly.FromDateTime(_clock.GetLocalNow().Date);
        ShowNewDayPrompt = false;
    }

    private async Task ApplyResetAsync(DateTimeOffset resetAt)
    {
        _settings.Current.ResetAt = resetAt;
        _settings.Save();

        ShowNewDayPrompt = false;
        _promptDismissedOn = null;

        await LoadAsync();
    }

    private DateTimeOffset EnsureResetAt()
    {
        if (_settings.Current.ResetAt is { } existing)
        {
            return existing;
        }

        // First run: today is the natural starting point.
        var today = new DateTimeOffset(_clock.GetLocalNow().Date, _clock.GetLocalNow().Offset);
        _settings.Current.ResetAt = today;
        _settings.Save();
        return today;
    }

    // ------------------------------------------------------------ live

    private void OnLiveChanged(LiveStatus status)
    {
        // Comes from the read loop, so from a foreign thread.
        _dispatcher.BeginInvoke(() => Apply(status));
    }

    private void Apply(LiveStatus status)
    {
        var previous = Status;
        Status = status;

        if (status.Rpm > 0)
        {
            _period.Add(
                _clock.GetUtcNow(),
                status.Rpm,
                status.RevolutionDelta,
                status.SessionStartedAt ?? previous.SessionStartedAt ?? _clock.GetUtcNow());

            Summary = _period.Summarize();
            SeriesChanged?.Invoke();
        }

        if (status.RevolutionDelta > 0)
        {
            CheckForNewDay();
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// When the first revolution of a day arrives while the statistics still run
    /// from yesterday, ask -- do not decide.
    /// </summary>
    private void CheckForNewDay()
    {
        if (ShowNewDayPrompt || _settings.Current.ResetAt is not { } since)
        {
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().Date);

        if (_promptDismissedOn == today || DateOnly.FromDateTime(since.ToLocalTime().Date) >= today)
        {
            return;
        }

        ShowNewDayPrompt = true;
    }

    // A new sample changes practically every display -- a single signal for all
    // bindings is more honest here than twenty attributes.
    partial void OnStatusChanged(LiveStatus value) => OnPropertyChanged(string.Empty);

    partial void OnSummaryChanged(PeriodSummary value) => OnPropertyChanged(string.Empty);

    public void Dispose() => _live.Changed -= OnLiveChanged;
}
