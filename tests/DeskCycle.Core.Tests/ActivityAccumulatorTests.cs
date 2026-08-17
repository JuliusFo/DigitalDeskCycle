using DeskCycle.Core.Statistics;

namespace DeskCycle.Core.Tests;

/// <summary>
/// The accumulator produces every figure of the live view and the same ones
/// again for the stored history. Both go through here, which is the point.
/// </summary>
public class ActivityAccumulatorTests
{
    private const double MetersPerRevolution = 6.67;

    private static readonly DateTimeOffset Start = new(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan PauseThreshold = TimeSpan.FromSeconds(30);

    private static ActivityAccumulator Create() => new(MetersPerRevolution, PauseThreshold);

    [Fact]
    public void Counts_active_time_from_the_gaps_between_samples()
    {
        var accumulator = Create();

        accumulator.Add(Start, rpm: 60, revolutionDelta: 0, Start);
        accumulator.Add(Start.AddSeconds(5), rpm: 60, revolutionDelta: 5, Start);
        accumulator.Add(Start.AddSeconds(10), rpm: 60, revolutionDelta: 5, Start);

        Assert.Equal(10, accumulator.ActiveSeconds);
        Assert.Equal(10, accumulator.Revolutions);
    }

    /// <summary>
    /// A pause drops out of the time axis entirely. Otherwise a lunch break
    /// would sit in the chart as an hour of riding at zero.
    /// </summary>
    [Fact]
    public void Drops_a_pause_from_the_time_axis_and_marks_it_instead()
    {
        var accumulator = Create();

        accumulator.Add(Start, rpm: 60, revolutionDelta: 0, Start);
        accumulator.Add(Start.AddSeconds(10), rpm: 60, revolutionDelta: 10, Start);

        // Ten minutes of standstill, then riding again.
        accumulator.Add(Start.AddMinutes(10), rpm: 60, revolutionDelta: 0, Start);
        accumulator.Add(Start.AddMinutes(10).AddSeconds(5), rpm: 60, revolutionDelta: 5, Start);

        Assert.Equal(15, accumulator.ActiveSeconds);
        Assert.Equal(new[] { 10d }, accumulator.PauseMarkers);
    }

    [Fact]
    public void Counts_a_gap_just_below_the_threshold_as_riding_time()
    {
        var accumulator = Create();

        accumulator.Add(Start, rpm: 60, revolutionDelta: 0, Start);
        accumulator.Add(Start.Add(PauseThreshold).AddSeconds(-1), rpm: 60, revolutionDelta: 29, Start);

        Assert.Equal(29, accumulator.ActiveSeconds);
        Assert.Empty(accumulator.PauseMarkers);
    }

    [Fact]
    public void Counts_a_session_per_distinct_start()
    {
        var accumulator = Create();
        var second = Start.AddHours(2);

        accumulator.Add(Start, rpm: 60, revolutionDelta: 0, Start);
        accumulator.Add(Start.AddSeconds(5), rpm: 60, revolutionDelta: 5, Start);
        accumulator.Add(second, rpm: 60, revolutionDelta: 0, second);

        Assert.Equal(2, accumulator.Sessions);
    }

    [Fact]
    public void Summarizes_distance_averages_and_peak()
    {
        var accumulator = Create();

        // Ten seconds at 60 rpm, ten more at 30. The steps stay below the pause
        // threshold, otherwise they would drop out of the time axis.
        accumulator.Add(Start, rpm: 60, revolutionDelta: 0, Start);
        accumulator.Add(Start.AddSeconds(10), rpm: 60, revolutionDelta: 10, Start);
        accumulator.Add(Start.AddSeconds(20), rpm: 30, revolutionDelta: 5, Start);

        var summary = accumulator.Summarize();

        Assert.Equal(15, summary.Revolutions);
        Assert.Equal(TimeSpan.FromSeconds(20), summary.ActiveDuration);
        Assert.Equal(15 * MetersPerRevolution, summary.DistanceMeters, 6);
        Assert.Equal(45, summary.AverageRpm, 6);                        // 15 revolutions in 20 seconds
        Assert.Equal(60 * MetersPerRevolution * 60 / 1000, summary.PeakSpeedKmh, 6);
        Assert.Equal(1, summary.Sessions);
        Assert.Equal(0, summary.Pauses);
    }

    /// <summary>Nothing ridden must not become a division by zero.</summary>
    [Fact]
    public void Summarizes_an_empty_period_as_zeroes()
    {
        var summary = Create().Summarize();

        Assert.Equal(0, summary.Revolutions);
        Assert.Equal(TimeSpan.Zero, summary.ActiveDuration);
        Assert.Equal(0, summary.AverageSpeedKmh);
        Assert.Equal(0, summary.AverageRpm);
        Assert.Equal(0, summary.Sessions);
    }
}
