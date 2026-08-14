namespace DeskCycle.Core.Tracking;

/// <summary>
/// One sample, regardless of which way it arrived.
/// </summary>
/// <param name="Count">Revolutions since the sensor started (cumulative). A value
/// that drops back counts as a restart of the Pico.</param>
/// <param name="Rpm">Cadence at the time of the sample.</param>
/// <param name="Bounce">Rejected edges, or null when the source does not provide
/// them. Rises during normal operation -- that is debouncing at work, not a
/// fault.</param>
/// <param name="Suspect">Pulses arriving implausibly close together, or null when
/// the source does not provide them. Over Bluetooth both are null: the CSC
/// profile carries only revolutions and a timestamp, no diagnostic counters.</param>
public readonly record struct CadenceReading(
    int Count,
    double Rpm,
    int? Bounce,
    int? Suspect);
