using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DeskCycle.Core.Data;
using DeskCycle.Core.Options;
using DeskCycle.Core.Statistics;
using DeskCycle.Core.Tracking;
using DeskCycle.Desktop.Logging;
using DeskCycle.Desktop.Services;
using DeskCycle.Desktop.Tracking;
using DeskCycle.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace DeskCycle.Desktop;

public partial class App : Application
{
    private IHost? _host;
    private TaskbarIcon? _trayIcon;
    private MainWindow? _window;
    private Mutex? _singleInstance;
    private bool _hintShown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Two instances would fight over the COM port and record twice -- that
        // only shows up in the data later, so catch it right here.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\DeskCycle.Desktop", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "DigitalDeskCycle läuft bereits. Das Fenster erreichst du über das Symbol in der Taskleiste.",
                "DigitalDeskCycle", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _host = BuildHost();
            RegisterExceptionHandlers();

            // Before the first window: otherwise it briefly shows in the startup
            // theme and then jumps.
            ThemeService.ApplySystemTheme();

            var settings = _host.Services.GetRequiredService<UserSettingsStore>();
            settings.Load();

            // What the settings page changed lies over the program's defaults.
            // Has to happen before anything reads the options -- the sources and
            // the recorder start right after this.
            TrackingSettingsBinder.Apply(
                settings.Current,
                _host.Services.GetRequiredService<IOptions<TrackingOptions>>().Value);

            await DatabaseStartup.PrepareAsync(_host.Services);
            await _host.StartAsync();

            if (settings.Current.ApiEnabled)
            {
                await _host.Services.GetRequiredService<ApiHostService>().StartAsync();
            }

            CreateTrayIcon();
            ShowMainWindow();
        }
        catch (Exception ex)
        {
            // If startup fails there is no logger yet -- without this emergency
            // exit the cause disappears without trace.
            using (var emergency = new FileLogWriter(AppPaths.LogDirectory))
            {
                emergency.Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} CRT Start | {ex}");
            }

            MessageBox.Show(
                $"DigitalDeskCycle konnte nicht starten.\n\n{ex.Message}\n\nEinzelheiten im Protokoll:\n{AppPaths.LogDirectory}",
                "DigitalDeskCycle", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void RegisterExceptionHandlers()
    {
        var logger = _host!.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled");

        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogCritical(args.Exception, "Unhandled error in the user interface.");

            MessageBox.Show(
                $"In der Oberfläche ist ein Fehler aufgetreten. Die Aufzeichnung läuft weiter.\n\n"
                + $"{args.Exception.Message}\n\nEinzelheiten im Protokoll:\n{AppPaths.LogDirectory}",
                "DigitalDeskCycle", MessageBoxButton.OK, MessageBoxImage.Warning);

            // An error while drawing a chart must not take the recording down
            // with it -- that is the application's actual job.
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.LogCritical(args.ExceptionObject as Exception, "Unhandled error.");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unobserved error in a background task.");
            args.SetObserved();
        };
    }

    private static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            Process.Start(new ProcessStartInfo(AppPaths.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // If even this fails, only the path from the message box helps.
        }
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            // Without this the host looks for appsettings.json in the current
            // working directory -- which is not the program directory when
            // started from a shortcut or through autostart.
            ContentRootPath = AppContext.BaseDirectory,
        });

        // A windowed application has no console: without a file, every message
        // from the host and the serial port goes nowhere.
        builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(AppPaths.LogDirectory));

        builder.Services.Configure<TrackingOptions>(
            builder.Configuration.GetSection(TrackingOptions.SectionName));

        AppPaths.EnsureDataDirectory();

        // Factory for the view models: those live as long as the window, a
        // DbContext should not. The additional scoped registration keeps
        // SessionRecorder and DatabaseStartup working unchanged.
        builder.Services.AddDbContextFactory<TrackerDbContext>(o => o.UseSqlite(AppPaths.ConnectionString));
        builder.Services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<TrackerDbContext>>().CreateDbContext());

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<LiveStatusService>();

        // Without a body weight the model says so itself and no figure is shown
        // anywhere. Swapping in a calibrated one later happens here.
        //
        // Resolved lazily, and the settings are loaded during startup before
        // anything asks for the model.
        builder.Services.AddSingleton<IEnergyModel>(sp =>
            new MetEnergyModel(() => sp.GetRequiredService<UserSettingsStore>().Current.BodyWeightKg));

        builder.Services.AddSingleton<SessionRecorder>();

        // Registration order is precedence: USB first, because only there do the
        // firmware's diagnostic counters come along. Radio is the fallback.
        builder.Services.AddSingleton<ICadenceSource, SerialCadenceSource>();
        builder.Services.AddSingleton<ICadenceSource, BluetoothCadenceSource>();
        builder.Services.AddHostedService<CadenceSourceCoordinator>();

        builder.Services.AddSingleton<PeriodStatisticsLoader>();
        builder.Services.AddSingleton<AutostartService>();
        builder.Services.AddSingleton<UserSettingsStore>();
        builder.Services.AddSingleton<ApiHostService>();
        builder.Services.AddSingleton<LiveViewModel>();
        builder.Services.AddSingleton<HistoryViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    /// <summary>
    /// Tray icon with a genuine WPF context menu. That way the Fluent templates
    /// the application already loaded apply -- a WinForms menu could be
    /// recoloured, but would keep the shape, the metrics and the missing rounded
    /// corners of the Windows 7 era.
    ///
    /// Deliberately short: autostart and data sharing live on the settings page,
    /// where they can show their state properly. Two places for one switch would
    /// only ever be two places to keep in step.
    /// </summary>
    private void CreateTrayIcon()
    {
        var showItem = new MenuItem { Header = "Fenster anzeigen" };
        showItem.Click += (_, _) => ShowMainWindow();

        var logItem = new MenuItem { Header = "Protokoll öffnen" };
        logItem.Click += (_, _) => OpenLogFolder();

        var exitItem = new MenuItem { Header = "Beenden" };
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new ContextMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(logItem);
        menu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            Icon = LoadTrayIcon(),
            ToolTipText = "DigitalDeskCycle",
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
        };
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();

        // Created in code rather than in XAML -- without this no icon appears.
        _trayIcon.ForceCreate();
    }

    /// <summary>
    /// Loaded straight as an Icon rather than through IconSource: H.NotifyIcon
    /// converts an ImageSource into a System.Drawing.Icon internally and fails on
    /// anything that is not an ICO stream.
    /// </summary>
    private static System.Drawing.Icon? LoadTrayIcon()
    {
        try
        {
            var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/deskcycle.ico"));
            if (resource is null)
            {
                return null;
            }

            using var stream = resource.Stream;
            return new System.Drawing.Icon(stream, 16, 16);
        }
        catch (Exception)
        {
            // Without its own icon the application runs just the same, only uglier.
            return null;
        }
    }

    private void ShowMainWindow()
    {
        _window ??= CreateMainWindow();

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private MainWindow CreateMainWindow()
    {
        var window = _host!.Services.GetRequiredService<MainWindow>();
        window.HiddenToTray += OnWindowHiddenToTray;
        return window;
    }

    private void OnWindowHiddenToTray(object? sender, EventArgs e)
    {
        if (_hintShown || _trayIcon is null)
        {
            return;
        }

        _hintShown = true;
        _trayIcon.ShowNotification(
            "DigitalDeskCycle läuft weiter",
            "Die Aufzeichnung läuft im Hintergrund. Beenden über das Symbol in der Taskleiste.",
            NotificationIcon.Info);
    }

    private async void ExitApplication()
    {
        // Make it vanish at once, rather than lingering until the process ends.
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (_host is not null)
        {
            await _host.Services.GetRequiredService<ApiHostService>().StopAsync();
        }

        _window?.AllowClose();
        _window?.Close();

        if (_host is not null)
        {
            // Closes a running session cleanly instead of leaving it open.
            await _host.StopAsync();
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _host?.Dispose();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }
}
