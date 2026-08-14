namespace DeskCycle.Core.Options;

public sealed class TrackingOptions
{
    public const string SectionName = "Tracking";

    /// <summary>
    /// Distance covered per crank revolution. A provisional estimate derived from
    /// the bike's original display (0.3 km over 45 revolutions) and therefore
    /// accurate to roughly +/-20 %. The better measuring procedure is described
    /// in firmware/README.md.
    ///
    /// The value is never written to the database -- revolutions are what gets
    /// stored. Correcting it later therefore applies retroactively to every
    /// session ever recorded.
    /// </summary>
    public double MetersPerRevolution { get; set; } = 6.67;

    /// <summary>COM port of the Pico. Empty = automatic, as long as there is exactly one.</summary>
    public string? SerialPort { get; set; }

    public int BaudRate { get; set; } = 115200;

    /// <summary>
    /// Device name the Pico advertises under. Must match BLE_NAME in the
    /// firmware. Discovery filters on the name rather than on the advertised
    /// service -- the service filter is not reliable on Windows.
    /// </summary>
    public string BluetoothDeviceName { get; set; } = "DeskCycle";

    /// <summary>No revolution for longer than this ends the session.</summary>
    public int SessionIdleTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Sessions below this number of revolutions are discarded when they end --
    /// guards against ghost rides from a pedal nudged in passing.
    /// </summary>
    public int MinimumSessionRevolutions { get; set; } = 10;

    /// <summary>
    /// A gap this long between two samples counts as a pause: it does not add to
    /// the active time and gets a tick in the history chart. Anything shorter is
    /// a hiccup, not a pause.
    /// </summary>
    public int PauseThresholdSeconds { get; set; } = 30;

    /// <summary>
    /// Upper end of the speed bar in the live view. Tied to the conversion
    /// factor: correct that one and this value may no longer fit either.
    /// </summary>
    public double SpeedGaugeMaxKmh { get; set; } = 40;
}
