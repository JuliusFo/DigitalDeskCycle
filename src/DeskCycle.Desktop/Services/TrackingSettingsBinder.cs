using DeskCycle.Core.Options;

namespace DeskCycle.Desktop.Services;

/// <summary>
/// Lays the user's settings over the program's defaults.
///
/// appsettings.json belongs to the program and an update overwrites it; what
/// the settings page changes therefore lands in settings.json. A value that was
/// never touched stays null there, so a new default from an update still
/// reaches whoever never had an opinion on it.
///
/// The options instance is mutated deliberately rather than rebuilt: everything
/// reads its values through the same singleton, so a change takes effect
/// wherever they are read fresh -- and the few places that copy them are
/// rebuilt by the caller.
/// </summary>
public static class TrackingSettingsBinder
{
    public static void Apply(UserSettings settings, TrackingOptions options)
    {
        if (settings.MetersPerRevolution is { } meters && meters > 0)
        {
            options.MetersPerRevolution = meters;
        }

        if (settings.SpeedGaugeMaxKmh is { } gauge && gauge > 0)
        {
            options.SpeedGaugeMaxKmh = gauge;
        }

        // Empty is a decision too: it means "find the port yourself".
        if (settings.SerialPort is not null)
        {
            options.SerialPort = settings.SerialPort;
        }

        if (!string.IsNullOrWhiteSpace(settings.BluetoothDeviceName))
        {
            options.BluetoothDeviceName = settings.BluetoothDeviceName;
        }
    }
}
