namespace DeskCycle.Core.Tracking;

/// <summary>
/// Holds the current live state and notifies whoever is interested.
///
/// The WPF views run in the same process and subscribe to <see cref="Changed"/>
/// directly -- they need no SignalR client for that. The hub exists for
/// consumers outside this process.
/// </summary>
public sealed class LiveStatusService
{
    private LiveStatus _current = new();

    public LiveStatus Current => _current;

    public event Action<LiveStatus>? Changed;

    public void Publish(LiveStatus status)
    {
        _current = status;
        Changed?.Invoke(status);
    }
}
