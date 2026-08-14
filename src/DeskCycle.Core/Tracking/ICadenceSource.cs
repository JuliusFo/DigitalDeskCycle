namespace DeskCycle.Core.Tracking;

/// <summary>
/// A source of samples -- serial port, Bluetooth, whatever comes next.
///
/// Sources are preferred in registration order; the
/// <see cref="CadenceSourceCoordinator"/> takes the first one that can be used.
/// </summary>
public interface ICadenceSource
{
    /// <summary>For logs and display, e.g. "USB" or "Bluetooth".</summary>
    string Name { get; }

    /// <summary>Which kind of link this is -- the display shows one icon per kind.</summary>
    CadenceSourceKind Kind { get; }

    /// <summary>
    /// Whether this source could take over right now. Must be cheap and must not
    /// connect: it is asked while another source is counting.
    ///
    /// false also covers "cannot say". Bluetooth would have to scan for an
    /// answer, and a scan running alongside a live connection is exactly what
    /// this is meant to avoid.
    /// </summary>
    bool CanTakeOver();

    /// <summary>
    /// Runs until the connection drops or the operation is cancelled.
    /// </summary>
    /// <returns>
    /// true when the source could actually be used -- even if it later broke off.
    /// false when it was unavailable from the start; the next source is tried in
    /// that case.
    /// </returns>
    Task<bool> TryRunAsync(CancellationToken cancellationToken);
}
