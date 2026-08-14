using System.Globalization;

namespace DeskCycle.Core.Tracking;

/// <summary>
/// Parses the line-based output of the Pico firmware; the format is described in
/// firmware/README.md.
///
/// Only CAD lines are evaluated. Lines starting with '#' are comments, PULSE and
/// BLE are diagnostic lines for troubleshooting the sensor and are dropped here.
/// </summary>
public static class CadenceLineParser
{
    private const int CadFieldCount = 6;

    public static bool TryParseCad(string? line, out CadenceReading reading)
    {
        reading = default;

        if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
        {
            return false;
        }

        var parts = line.Split(',');
        if (parts.Length != CadFieldCount || parts[0] != "CAD")
        {
            return false;
        }

        // The firmware always writes a point as the decimal separator.
        var culture = CultureInfo.InvariantCulture;

        // Field 1 is the firmware's uptime in milliseconds. It is validated but
        // not carried over: this machine's clock is what counts, so that samples
        // from different sources stay comparable.
        if (!long.TryParse(parts[1], NumberStyles.Integer, culture, out _) ||
            !int.TryParse(parts[2], NumberStyles.Integer, culture, out var count) ||
            !double.TryParse(parts[3], NumberStyles.Float, culture, out var rpm) ||
            !int.TryParse(parts[4], NumberStyles.Integer, culture, out var bounce) ||
            !int.TryParse(parts[5], NumberStyles.Integer, culture, out var suspect))
        {
            return false;
        }

        reading = new CadenceReading(count, rpm, bounce, suspect);
        return true;
    }
}
