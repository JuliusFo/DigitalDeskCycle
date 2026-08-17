using DeskCycle.Core.Statistics;

namespace DeskCycle.Core.Tests;

/// <summary>
/// The figure is an estimate; what these tests hold to is that it behaves
/// sensibly -- more cadence and more time cost more, standing still costs
/// nothing, and without a body weight nothing is claimed at all.
/// </summary>
public class MetEnergyModelTests
{
    private const double BodyWeightKg = 80;

    private static MetEnergyModel Create(double weight = BodyWeightKg) => new(weight);

    [Fact]
    public void Says_nothing_without_a_body_weight()
    {
        var model = Create(weight: 0);

        Assert.False(model.IsConfigured);
        Assert.Null(model.Kcal(60, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Standing_still_costs_nothing()
    {
        Assert.Equal(0, Create().Kcal(rpm: 0, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Zero_duration_costs_nothing()
    {
        Assert.Equal(0, Create().Kcal(rpm: 60, TimeSpan.Zero));
    }

    [Fact]
    public void More_cadence_costs_more()
    {
        var model = Create();
        var duration = TimeSpan.FromMinutes(10);

        var slow = model.Kcal(40, duration);
        var brisk = model.Kcal(70, duration);
        var hard = model.Kcal(100, duration);

        Assert.True(slow < brisk);
        Assert.True(brisk < hard);
    }

    [Fact]
    public void Twice_the_time_costs_twice_as_much()
    {
        var model = Create();

        var ten = model.Kcal(60, TimeSpan.FromMinutes(10))!.Value;
        var twenty = model.Kcal(60, TimeSpan.FromMinutes(20))!.Value;

        Assert.Equal(ten * 2, twenty, 6);
    }

    [Fact]
    public void Twice_the_weight_costs_twice_as_much()
    {
        var duration = TimeSpan.FromMinutes(10);

        var light = Create(weight: 60).Kcal(60, duration)!.Value;
        var heavy = Create(weight: 120).Kcal(60, duration)!.Value;

        Assert.Equal(light * 2, heavy, 6);
    }

    /// <summary>
    /// The cadence sits between two points of the curve. A stepped model would
    /// return the same figure for both, and the display would jump at the
    /// boundary.
    /// </summary>
    [Fact]
    public void Interpolates_between_the_points_of_the_curve()
    {
        var model = Create();
        var duration = TimeSpan.FromMinutes(10);

        var at50 = model.Kcal(50, duration)!.Value;
        var at60 = model.Kcal(60, duration)!.Value;
        var at70 = model.Kcal(70, duration)!.Value;

        Assert.True(at50 < at60 && at60 < at70);

        // 60 sits exactly halfway between the points at 50 and 70.
        Assert.Equal((at50 + at70) / 2, at60, 6);
    }

    /// <summary>
    /// 60 rpm for an hour at 80 kg. The curve gives 4.25 MET there, minus one
    /// for the resting metabolism: 3.25 x 3.5 x 80 / 200 = 4.55 kcal a minute,
    /// so 273 an hour. Held here so that a change to the curve has to be a
    /// deliberate one -- and as a sanity check on the order of magnitude.
    /// </summary>
    [Fact]
    public void Produces_the_expected_magnitude_for_an_hour_of_riding()
    {
        var kcal = Create().Kcal(60, TimeSpan.FromHours(1))!.Value;

        Assert.Equal(273, kcal, 0);
    }
}
