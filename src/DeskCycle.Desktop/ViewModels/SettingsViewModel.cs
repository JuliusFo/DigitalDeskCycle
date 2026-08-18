using CommunityToolkit.Mvvm.ComponentModel;

using DeskCycle.Core.Options;
using DeskCycle.Desktop.Services;
using Microsoft.Extensions.Options;

namespace DeskCycle.Desktop.ViewModels;

/// <summary>
/// The settings page. Everything it changes lands in settings.json, which
/// survives an update -- appsettings.json belongs to the program.
///
/// Changes take effect immediately wherever that is possible. Where it is not,
/// the page says so instead of pretending: the port and the device name are
/// picked up when the source next reconnects, not while it is counting.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly UserSettingsStore _store;
    private readonly TrackingOptions _options;
    private readonly ApiHostService _api;
    private readonly AutostartService _autostart;

    /// <summary>Guards against the switch reacting to its own correction.</summary>
    private bool _settingApiQuietly;

    /// <summary>Raised when a change needs the live view recalculated.</summary>
    public event Action? FiguresChanged;

    public SettingsViewModel(
        UserSettingsStore store,
        IOptions<TrackingOptions> options,
        ApiHostService api,
        AutostartService autostart)
    {
        _store = store;
        _options = options.Value;
        _api = api;
        _autostart = autostart;

        _bodyWeightKg = store.Current.BodyWeightKg > 0 ? store.Current.BodyWeightKg : null;
        _metersPerRevolution = _options.MetersPerRevolution;
        _speedGaugeMaxKmh = _options.SpeedGaugeMaxKmh;
        _serialPort = _options.SerialPort ?? string.Empty;
        _bluetoothDeviceName = _options.BluetoothDeviceName;
        _apiPort = store.Current.ApiPort;
        _apiAllowRemote = store.Current.ApiAllowRemote;
        _apiEnabled = api.IsRunning;
        _autostartEnabled = autostart.IsEnabled;
    }

    // ------------------------------------------------------------ measurement

    /// <summary>null = not stated, then no calorie figure is shown anywhere.</summary>
    [ObservableProperty]
    private double? _bodyWeightKg;

    [ObservableProperty]
    private double _metersPerRevolution;

    [ObservableProperty]
    private double _speedGaugeMaxKmh;

    // ------------------------------------------------------------ connection

    /// <summary>Empty = find the port automatically, as long as there is exactly one.</summary>
    [ObservableProperty]
    private string _serialPort;

    [ObservableProperty]
    private string _bluetoothDeviceName;

    // ------------------------------------------------------------ web server

    [ObservableProperty]
    private bool _apiEnabled;

    [ObservableProperty]
    private int _apiPort;

    [ObservableProperty]
    private bool _apiAllowRemote;

    [ObservableProperty]
    private bool _autostartEnabled;

    /// <summary>What went wrong last, empty while nothing did.</summary>
    [ObservableProperty]
    private string _apiProblem = string.Empty;

    public bool HasApiProblem => !string.IsNullOrEmpty(ApiProblem);

    public string ApiStatusText => _api.IsRunning
        ? $"Läuft auf {_api.Url}"
        : "Aus — niemand kann die Daten abrufen";

    public bool IsApiRunning => _api.IsRunning;

    // ------------------------------------------------------------ changes

    partial void OnBodyWeightKgChanged(double? value)
    {
        _store.Current.BodyWeightKg = value is > 0 ? value.Value : 0;
        Save();
        FiguresChanged?.Invoke();
    }

    partial void OnMetersPerRevolutionChanged(double value)
    {
        if (value <= 0)
        {
            return;
        }

        _store.Current.MetersPerRevolution = value;
        _options.MetersPerRevolution = value;
        Save();
        FiguresChanged?.Invoke();
    }

    partial void OnSpeedGaugeMaxKmhChanged(double value)
    {
        if (value <= 0)
        {
            return;
        }

        _store.Current.SpeedGaugeMaxKmh = value;
        _options.SpeedGaugeMaxKmh = value;
        Save();
        FiguresChanged?.Invoke();
    }

    partial void OnSerialPortChanged(string value)
    {
        _store.Current.SerialPort = value;
        _options.SerialPort = value;
        Save();
    }

    partial void OnBluetoothDeviceNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _store.Current.BluetoothDeviceName = value;
        _options.BluetoothDeviceName = value;
        Save();
    }

    partial void OnApiPortChanged(int value)
    {
        if (value is < 1 or > 65535)
        {
            return;
        }

        _store.Current.ApiPort = value;
        Save();
        OnPropertyChanged(nameof(ApiStatusText));
    }

    partial void OnApiAllowRemoteChanged(bool value)
    {
        _store.Current.ApiAllowRemote = value;
        Save();
        OnPropertyChanged(nameof(ApiStatusText));
    }

    partial void OnAutostartEnabledChanged(bool value) => _autostart.SetEnabled(value);

    partial void OnApiProblemChanged(string value) => OnPropertyChanged(nameof(HasApiProblem));

    /// <summary>
    /// Starting and stopping happen on the switch itself. The server can
    /// refuse -- a taken port, most likely -- and then the switch has to fall
    /// back instead of claiming something that is not running.
    /// </summary>
    partial void OnApiEnabledChanged(bool value)
    {
        // Set by the code below rather than by the switch: then the
        // server has already done what it was going to do.
        if (_settingApiQuietly)
        {
            return;
        }

        _ = ApplyApiAsync(value);
    }

    private async Task ApplyApiAsync(bool enabled)
    {
        ApiProblem = string.Empty;

        if (enabled)
        {
            if (!await _api.StartAsync())
            {
                ApiProblem = $"Der Webserver konnte auf Port {ApiPort} nicht starten. Vermutlich ist der Port belegt.";
                SetApiEnabledQuietly(false);
            }
        }
        else
        {
            await _api.StopAsync();
        }

        _store.Current.ApiEnabled = _api.IsRunning;
        Save();

        OnPropertyChanged(nameof(ApiStatusText));
        OnPropertyChanged(nameof(IsApiRunning));
    }

    /// <summary>Puts the switch back without starting or stopping anything.</summary>
    private void SetApiEnabledQuietly(bool value)
    {
        _settingApiQuietly = true;
        ApiEnabled = value;
        _settingApiQuietly = false;
    }

    /// <summary>Follows along when something else changes these -- the tray menu, say.</summary>
    public void Refresh()
    {
        SetApiEnabledQuietly(_api.IsRunning);
        AutostartEnabled = _autostart.IsEnabled;

        OnPropertyChanged(nameof(ApiStatusText));
        OnPropertyChanged(nameof(IsApiRunning));
    }

    private void Save() => _store.Save();
}
