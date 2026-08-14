namespace DeskCycle.Core.Tracking;

/// <summary>
/// Snapshot for the live display. Goes unchanged to the WPF view, to the
/// SignalR hub and to GET /api/live.
/// </summary>
public sealed record LiveStatus
{
    public bool SensorConnected { get; init; }

    /// <summary>Which source is counting, e.g. "COM3" or "Bluetooth (DeskCycle)".</summary>
    public string? SourceName { get; init; }

    public bool SessionActive { get; init; }

    public DateTimeOffset? SessionStartedAt { get; init; }

    public TimeSpan SessionDuration { get; init; }

    public int SessionRevolutions { get; init; }

    /// <summary>
    /// Revolutions since the previous sample. Lets displays carry their totals
    /// forward instead of re-aggregating them from the database every second.
    /// </summary>
    public int RevolutionDelta { get; init; }

    public double Rpm { get; init; }

    public double SpeedKmh { get; init; }

    public double DistanceMeters { get; init; }

    /// <summary>
    /// Diagnostic counter from the firmware. While it stays at 0 the sensor
    /// counts every revolution exactly once. If it climbs, check the reed switch.
    ///
    /// null means "the current source does not provide this" -- over Bluetooth
    /// the value does not exist. Deliberately not represented as 0: that would
    /// look like a clean measurement nobody took.
    /// </summary>
    public int? SuspectCount { get; init; }
}
