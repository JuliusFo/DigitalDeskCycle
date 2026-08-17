namespace DeskCycle.Core.Statistics;

/// <summary>
/// Estimates the energy turnover from body weight and cadence, by the usual MET
/// formula: kcal per minute = MET x 3.5 x kg / 200.
///
/// **This is an estimate, not a measurement.** Calories follow from mechanical
/// power, and the application cannot see power: the resistance of the bike is
/// set mechanically and looks identical in the signal at every setting. Sixty
/// revolutions a minute against the lightest resistance and against the
/// heaviest are the same number here, and energetically a multiple apart.
/// Expect the figure to be off by anywhere up to half.
///
/// The MET values are the ones for cycling, adjusted downwards: a desk bike
/// under a table is ridden more gently than a road bike, and without body
/// weight to carry. Interpolated between the points rather than stepped, so the
/// display does not jump when the cadence crosses a boundary.
///
/// Counted net of the resting metabolism (one MET is subtracted): what is meant
/// is what the riding costs on top, not what sitting there costs anyway.
/// </summary>
public sealed class MetEnergyModel(double bodyWeightKg) : IEnergyModel
{
    /// <summary>Cadence in revolutions per minute against the MET value at that cadence.</summary>
    private static readonly (double Rpm, double Met)[] Curve =
    [
        (0, 1.3),       // sitting, feet on the pedals
        (30, 2.5),      // turning them over
        (50, 3.5),      // light, the usual pace at a desk
        (70, 5.0),      // brisk
        (90, 6.5),      // sweating
        (110, 8.0),     // not for long
    ];

    public bool IsConfigured => bodyWeightKg > 0;

    public double? Kcal(double rpm, TimeSpan duration, int? resistanceLevel = null)
    {
        if (!IsConfigured)
        {
            return null;
        }

        if (rpm <= 0 || duration <= TimeSpan.Zero)
        {
            return 0;
        }

        // Net of the resting metabolism, and never below zero: at a standstill
        // the curve is under one MET.
        var net = Math.Max(0, MetFor(rpm) - 1);

        return net * 3.5 * bodyWeightKg / 200 * duration.TotalMinutes;
    }

    private static double MetFor(double rpm)
    {
        if (rpm <= Curve[0].Rpm)
        {
            return Curve[0].Met;
        }

        for (var i = 1; i < Curve.Length; i++)
        {
            if (rpm > Curve[i].Rpm)
            {
                continue;
            }

            var (fromRpm, fromMet) = Curve[i - 1];
            var (toRpm, toMet) = Curve[i];

            var position = (rpm - fromRpm) / (toRpm - fromRpm);
            return fromMet + position * (toMet - fromMet);
        }

        // Beyond the last point: carry on at the slope of the last segment
        // rather than capping. Whoever pedals that fast should not stop earning.
        var (lastRpm, lastMet) = Curve[^1];
        var (previousRpm, previousMet) = Curve[^2];
        var slope = (lastMet - previousMet) / (lastRpm - previousRpm);

        return lastMet + (rpm - lastRpm) * slope;
    }
}
