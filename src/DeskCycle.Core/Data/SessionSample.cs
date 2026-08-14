namespace DeskCycle.Core.Data;

/// <summary>
/// One sample, taken once per second while moving. Standstill is not recorded;
/// pauses fall out of the gaps between timestamps when reading the data back.
/// </summary>
public class SessionSample
{
    public long Id { get; set; }

    public int SessionId { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Revolutions since the start of this session, not since the Pico booted.</summary>
    public int Revolutions { get; set; }

    public double Rpm { get; set; }

    public Session? Session { get; set; }
}
