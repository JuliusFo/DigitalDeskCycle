using CommunityToolkit.Mvvm.ComponentModel;
using DeskCycle.Core.Tracking;
using Wpf.Ui.Controls;

namespace DeskCycle.Desktop.ViewModels;

/// <summary>How a single link is presented.</summary>
public enum LinkState
{
    /// <summary>Nothing is connected at all -- then every link is shown in red.</summary>
    Offline,

    /// <summary>Another link is counting. Whether this one could be used is unknown.</summary>
    Idle,

    /// <summary>This link is delivering the samples.</summary>
    Active,
}

/// <summary>
/// One icon in the connection display, one instance per
/// <see cref="CadenceSourceKind"/>.
///
/// Deliberately updated in place rather than recreated: the live values arrive
/// once a second, and rebuilding the elements would take every open tooltip
/// down with them.
///
/// Only ever knows two things -- who is counting right now and whether anything
/// is counting at all. The application connects a single source at a time, so
/// "available but unused" is a state nobody can honestly report without
/// scanning behind the active connection.
/// </summary>
public sealed partial class LinkIndicatorViewModel(
    CadenceSourceKind kind, string label, SymbolRegular symbol) : ObservableObject
{
    public SymbolRegular Symbol { get; } = symbol;

    [ObservableProperty]
    private LinkState _state = LinkState.Offline;

    [ObservableProperty]
    private string _tooltip = string.Empty;

    public void Update(LiveStatus status)
    {
        if (!status.SensorConnected)
        {
            State = LinkState.Offline;
            Tooltip = $"{label}: kein Sensor — hängt der Pico am Strom?";
            return;
        }

        if (status.SourceKind != kind)
        {
            State = LinkState.Idle;
            Tooltip = $"{label} wird gerade nicht genutzt";
            return;
        }

        State = LinkState.Active;
        Tooltip = status.SourceName is null
            ? $"{label} verbunden"
            : $"{label} verbunden · {status.SourceName}";
    }
}
