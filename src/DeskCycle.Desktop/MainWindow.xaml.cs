using System.ComponentModel;
using DeskCycle.Desktop.Services;
using DeskCycle.Desktop.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DeskCycle.Desktop;

public partial class MainWindow : FluentWindow
{
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Follows along when Windows switches between light and dark while the
        // application is running.
        SystemThemeWatcher.Watch(this, ThemeService.Backdrop, true);
    }

    /// <summary>
    /// Raised when the user closes the window while the application keeps
    /// running -- so that the caller can point this out once.
    /// </summary>
    public event EventHandler? HiddenToTray;

    /// <summary>Only through "Beenden" in the tray menu.</summary>
    public void AllowClose() => _allowClose = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing only hides the window. Recording is meant to continue
        // throughout the working day without a window being left open.
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }
}
