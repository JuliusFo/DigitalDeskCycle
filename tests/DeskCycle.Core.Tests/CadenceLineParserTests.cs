using System.Globalization;
using DeskCycle.Core.Tracking;

namespace DeskCycle.Core.Tests;

/// <summary>
/// The line format is the boundary to the firmware. Whatever gets through here
/// wrong ends up in the database as a measurement nobody took.
/// </summary>
public class CadenceLineParserTests
{
    [Fact]
    public void Reads_all_fields_of_a_cad_line()
    {
        var parsed = CadenceLineParser.TryParseCad("CAD,123456,42,55.5,1,2", out var reading);

        Assert.True(parsed);
        Assert.Equal(42, reading.Count);
        Assert.Equal(55.5, reading.Rpm);
        Assert.Equal(1, reading.Bounce);
        Assert.Equal(2, reading.Suspect);
    }

    /// <summary>
    /// The firmware always writes a point. On a German system a comma-reading
    /// parser would turn 55.5 into 555 -- a tenfold cadence.
    /// </summary>
    [Fact]
    public void Reads_the_decimal_point_regardless_of_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            Assert.True(CadenceLineParser.TryParseCad("CAD,1000,10,55.5,0,0", out var reading));
            Assert.Equal(55.5, reading.Rpm);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null)]                          // nothing at all
    [InlineData("")]                            // empty line
    [InlineData("   ")]                         // whitespace
    [InlineData("# comment")]                   // comment from the firmware
    [InlineData("PULSE,1000,3")]                // diagnostic line
    [InlineData("BLE,connected")]               // diagnostic line
    [InlineData("CAD,1000,10,55.5,0")]          // one field short
    [InlineData("CAD,1000,10,55.5,0,0,0")]      // one field too many
    [InlineData("CAD,1000,ten,55.5,0,0")]       // count is not a number
    [InlineData("CAD,1000,10,fast,0,0")]        // rpm is not a number
    [InlineData("CAD,up,10,55.5,0,0")]          // uptime is not a number
    public void Rejects_everything_that_is_not_a_complete_cad_line(string? line)
    {
        Assert.False(CadenceLineParser.TryParseCad(line, out _));
    }

    /// <summary>
    /// Over Bluetooth the diagnostic counters do not exist. Zero would look like
    /// a clean measurement, so on the serial side a real 0 has to stay a 0.
    /// </summary>
    [Fact]
    public void Keeps_a_counter_of_zero_as_zero_rather_than_null()
    {
        Assert.True(CadenceLineParser.TryParseCad("CAD,1000,10,55.5,0,0", out var reading));

        Assert.Equal(0, reading.Bounce);
        Assert.Equal(0, reading.Suspect);
    }
}
