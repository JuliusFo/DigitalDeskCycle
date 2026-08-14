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
