namespace DeskCycle.Desktop.ViewModels;

/// <summary>Holds the three areas of the main window together.</summary>
public sealed class MainViewModel
{
    public MainViewModel(LiveViewModel live, HistoryViewModel history, SettingsViewModel settings)
    {
        Live = live;
        History = history;
        Settings = settings;

        // A corrected conversion factor or body weight changes every figure of
        // the period, so the live view is rebuilt from the database rather than
        // patched in place.
        settings.FiguresChanged += async () => await live.LoadAsync();
    }

    public LiveViewModel Live { get; }

    public HistoryViewModel History { get; }

    public SettingsViewModel Settings { get; }
}
