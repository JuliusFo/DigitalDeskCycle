namespace DeskCycle.Core.Statistics;

/// <summary>
/// Estimates energy turnover from what the sensor can actually see.
///
/// Deliberately an interface with room for the resistance level, although the
/// model behind it today ignores it: the bike's setting is purely mechanical
/// and invisible in the signal, so a calibrated model needs it as soon as one
/// exists. Every call site already passes what it knows.
/// </summary>
public interface IEnergyModel
{
    /// <summary>
    /// Whether the model can produce a number at all. Without a body weight it
    /// cannot -- and an invented default person would be a figure nobody asked
    /// for.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Kilocalories for a stretch ridden at <paramref name="rpm"/>.
    /// </summary>
    /// <param name="resistanceLevel">
    /// The mechanical setting of the bike, null when unknown -- which is the
    /// normal case while riding, because it is entered by hand afterwards.
    /// </param>
    /// <returns>null when <see cref="IsConfigured"/> is false.</returns>
    double? Kcal(double rpm, TimeSpan duration, int? resistanceLevel = null);
}
