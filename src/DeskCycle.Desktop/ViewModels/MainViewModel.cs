namespace DeskCycle.Desktop.ViewModels;

/// <summary>Holds the two areas of the main window together.</summary>
public sealed class MainViewModel(LiveViewModel live, HistoryViewModel history)
{
    public LiveViewModel Live { get; } = live;

    public HistoryViewModel History { get; } = history;
}
