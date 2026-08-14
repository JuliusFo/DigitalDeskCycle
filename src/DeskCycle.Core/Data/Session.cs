namespace DeskCycle.Core.Data;

/// <summary>One continuous training session.</summary>
public class Session
{
    public int Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>null while the session is still running.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Revolutions of this session. Deliberately the stored quantity: distance
    /// and speed are derived from it and change along when the conversion factor
    /// is measured more precisely later.
    /// </summary>
    public int Revolutions { get; set; }

    /// <summary>
    /// Resistance level, entered by hand. The adjustment on the bike is purely
    /// mechanical and invisible in the signal -- this is the basis for a future
    /// power estimate.
    /// </summary>
    public int? ResistanceLevel { get; set; }

    public string? Note { get; set; }

    public List<SessionSample> Samples { get; set; } = [];
}
