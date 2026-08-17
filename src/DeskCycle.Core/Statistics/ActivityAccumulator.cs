namespace DeskCycle.Core.Statistics;

/// <summary>One point of the history curve: position on the active time axis and speed.</summary>
public readonly record struct ActivityPoint(double ActiveSeconds, double SpeedKmh);

public sealed record PeriodSummary(
    int Revolutions,
    TimeSpan ActiveDuration,
    double DistanceMeters,
    double AverageSpeedKmh,
    double AverageRpm,
    double PeakSpeedKmh,
    int Sessions,
    int Pauses,
    /// <summary>null when no body weight is stated -- see <see cref="IEnergyModel"/>.</summary>
    double? CaloriesKcal = null);

/// <summary>
/// Builds the history curve and the summary figures for a period.
///
/// Deliberately a state that can be carried forward rather than a query: the
/// same procedure processes the stored samples when the view opens and the ones
/// arriving live afterwards. A second procedure for the running case would
/// sooner or later produce different numbers than the first.
///
/// The time axis is *active* time: pauses are taken out and only noted as a
/// marker. A point in time can no longer be read off it -- the curve answers
/// "how did I ride", not "when".
/// </summary>
public sealed class ActivityAccumulator(
    double metersPerRevolution, TimeSpan pauseThreshold, IEnergyModel? energy = null)
{
    private readonly List<ActivityPoint> _points = [];
    private readonly List<double> _pauseMarkers = [];

    private DateTimeOffset? _lastTimestamp;
    private DateTimeOffset? _lastSessionStart;
    private double _lastRpm;
    private double _kcal;

    public IReadOnlyList<ActivityPoint> Points => _points;

    /// <summary>Positions of the pauses on the active time axis, in seconds.</summary>
    public IReadOnlyList<double> PauseMarkers => _pauseMarkers;

    public double ActiveSeconds { get; private set; }

    public int Revolutions { get; private set; }

    public double PeakSpeedKmh { get; private set; }

    public int Sessions { get; private set; }

    /// <param name="revolutionDelta">
    /// Revolutions since the previous sample. 0 for the first sample of a session
    /// within the period -- whatever came before does not belong to it.
    /// </param>
    public void Add(
        DateTimeOffset timestamp, double rpm, int revolutionDelta, DateTimeOffset sessionStart)
    {
        if (_lastSessionStart != sessionStart)
        {
            Sessions++;
            _lastSessionStart = sessionStart;
        }

        if (_lastTimestamp is not null)
        {
            var gap = timestamp - _lastTimestamp.Value;

            if (gap >= pauseThreshold)
            {
                // The gap drops out of the time axis and leaves only a marker at
                // the position where it began.
                _pauseMarkers.Add(ActiveSeconds);
            }
            else if (gap > TimeSpan.Zero)
            {
                ActiveSeconds += gap.TotalSeconds;

                // Over the gap the cadence went from the previous value to this
                // one; the mean is closer than either end. A pause earns
                // nothing, which is why this sits in the else branch.
                _kcal += energy?.Kcal((_lastRpm + rpm) / 2, gap) ?? 0;
            }
        }

        Revolutions += revolutionDelta;

        var speedKmh = rpm * metersPerRevolution * 60 / 1000;
        if (speedKmh > PeakSpeedKmh)
        {
            PeakSpeedKmh = speedKmh;
        }

        _points.Add(new ActivityPoint(ActiveSeconds, speedKmh));
        _lastTimestamp = timestamp;
        _lastRpm = rpm;
    }

    public PeriodSummary Summarize()
    {
        var hours = ActiveSeconds / 3600.0;
        var minutes = ActiveSeconds / 60.0;
        var distanceMeters = Revolutions * metersPerRevolution;

        return new PeriodSummary(
            Revolutions,
            TimeSpan.FromSeconds(ActiveSeconds),
            distanceMeters,
            hours > 0 ? distanceMeters / 1000 / hours : 0,
            minutes > 0 ? Revolutions / minutes : 0,
            PeakSpeedKmh,
            Sessions,
            _pauseMarkers.Count,
            energy?.IsConfigured == true ? _kcal : null);
    }
}
