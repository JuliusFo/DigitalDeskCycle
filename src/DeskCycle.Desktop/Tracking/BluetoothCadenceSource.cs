using System.Buffers.Binary;
using System.Threading.Channels;
using DeskCycle.Core.Options;
using DeskCycle.Core.Tracking;
using InTheHand.Bluetooth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeskCycle.Desktop.Tracking;

/// <summary>
/// Reads crank revolutions over the BLE standard profile "Cycling Speed and
/// Cadence" (service 0x1816).
///
/// Second in line behind the serial port: the profile carries only cumulative
/// revolutions and a timestamp. The firmware's diagnostic counters --
/// <c>bounce</c> and <c>suspect</c> -- do not exist here, and accordingly the
/// warning about implausible pulses stays silent on radio.
///
/// Lives in the desktop project rather than the core: the BLE library needs a
/// Windows target framework, and the core is meant to stay platform-neutral.
/// </summary>
public sealed class BluetoothCadenceSource(
    SessionRecorder recorder,
    IOptions<TrackingOptions> options,
    TimeProvider clock,
    ILogger<BluetoothCadenceSource> logger) : ICadenceSource
{
    private static readonly BluetoothUuid CscService = BluetoothUuid.FromShortId(0x1816);
    private static readonly BluetoothUuid CscMeasurement = BluetoothUuid.FromShortId(0x2A5B);

    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The firmware reports once a second. A long silence means the link is dead.</summary>
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(10);

    /// <summary>No new revolution for longer than this means cadence 0.</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(4);

    private readonly TrackingOptions _options = options.Value;

    private ushort? _lastRevolutions;
    private ushort? _lastEventTicks;
    private DateTimeOffset _lastChangeAt;
    private double _lastGapMs;

    public string Name => "Bluetooth";

    public CadenceSourceKind Kind => CadenceSourceKind.Bluetooth;

    /// <summary>
    /// Always false: whether the bike is within reach only a scan would tell,
    /// and scanning next to a running connection is not worth it. Radio is the
    /// fallback anyway -- nothing below it that it could take over from.
    /// </summary>
    public bool CanTakeOver() => false;

    public async Task<bool> TryRunAsync(CancellationToken cancellationToken)
    {
        if (!await Bluetooth.GetAvailabilityAsync())
        {
            return false;
        }

        var device = await FindDeviceAsync(cancellationToken);
        if (device is null)
        {
            return false;
        }

        await device.Gatt.ConnectAsync();
        if (!device.Gatt.IsConnected)
        {
            logger.LogInformation("Connection to {Device} could not be established.", device.Name);
            return false;
        }

        var service = await device.Gatt.GetPrimaryServiceAsync(CscService);
        var measurement = service is null ? null : await service.GetCharacteristicAsync(CscMeasurement);

        if (measurement is null)
        {
            logger.LogWarning("{Device} does not offer the expected CSC service.", device.Name);
            device.Gatt.Disconnect();
            return false;
        }

        // The device name alone: that it came over radio is carried by the kind.
        var sourceName = device.Name ?? _options.BluetoothDeviceName;
        logger.LogInformation("Sensor connected over Bluetooth with {Device}.", sourceName);

        ResetCadenceState();
        recorder.OnSourceConnected();

        // In a finally: a handover to USB cancels the pump, and a link left
        // connected would get in the way of the next scan.
        try
        {
            await PumpAsync(measurement, sourceName, cancellationToken);
        }
        finally
        {
            try
            {
                device.Gatt.Disconnect();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Disconnecting the radio link failed.");
            }
        }

        return true;
    }

    private async Task<BluetoothDevice?> FindDeviceAsync(CancellationToken cancellationToken)
    {
        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scanCts.CancelAfter(ScanTimeout);

        var scanOptions = new RequestDeviceOptions();
        scanOptions.Filters.Add(new BluetoothLEScanFilter { Services = { CscService } });

        var devices = await Bluetooth.ScanForDevicesAsync(scanOptions, scanCts.Token);

        // Select by name: the service filter above does not really narrow things
        // down on Windows, discovery returns foreign and unnamed devices too.
        return devices.FirstOrDefault(d =>
            string.Equals(d.Name, _options.BluetoothDeviceName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Notifications arrive on a foreign thread. They therefore travel through a
    /// channel, so that the SessionRecorder -- deliberately not thread safe --
    /// keeps being served from exactly one thread.
    /// </summary>
    private async Task PumpAsync(
        GattCharacteristic measurement, string sourceName, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<CadenceReading>(
            new UnboundedChannelOptions { SingleReader = true });

        void OnValueChanged(object? sender, GattCharacteristicValueChangedEventArgs args)
        {
            if (TryConvert(args.Value, out var reading))
            {
                channel.Writer.TryWrite(reading);
            }
        }

        measurement.CharacteristicValueChanged += OnValueChanged;

        try
        {
            await measurement.StartNotificationsAsync();

            while (!cancellationToken.IsCancellationRequested)
            {
                using var silenceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                silenceCts.CancelAfter(SilenceTimeout);

                CadenceReading reading;
                try
                {
                    reading = await channel.Reader.ReadAsync(silenceCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("No more radio notifications, treating the link as dropped.");
                    break;
                }

                await recorder.OnReadingAsync(reading, Kind, sourceName, cancellationToken);
            }
        }
        finally
        {
            measurement.CharacteristicValueChanged -= OnValueChanged;

            try
            {
                await measurement.StopNotificationsAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Unsubscribing from notifications failed.");
            }
        }
    }

    private void ResetCadenceState()
    {
        _lastRevolutions = null;
        _lastEventTicks = null;
        _lastGapMs = 0;
        _lastChangeAt = default;
    }

    /// <summary>
    /// Converts a CSC measurement packet. Layout: one flags byte, then --
    /// depending on the flags -- wheel data (6 bytes) and crank data (2 bytes of
    /// revolutions, 2 bytes of timestamp in 1/1024 seconds).
    /// </summary>
    private bool TryConvert(byte[]? data, out CadenceReading reading)
    {
        reading = default;

        if (data is null || data.Length < 1)
        {
            return false;
        }

        var flags = data[0];
        if ((flags & 0x02) == 0)
        {
            return false;   // contains no crank data
        }

        var offset = (flags & 0x01) != 0 ? 7 : 1;
        if (data.Length < offset + 4)
        {
            return false;
        }

        var revolutions = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        var eventTicks = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2));
        var now = clock.GetUtcNow();

        if (_lastRevolutions is not null && revolutions != _lastRevolutions)
        {
            // Both values are 16 bit and roll over; computed as ushort the
            // difference still comes out right.
            var deltaRevolutions = (ushort)(revolutions - _lastRevolutions.Value);
            var deltaTicks = (ushort)(eventTicks - _lastEventTicks.GetValueOrDefault());

            if (deltaRevolutions > 0 && deltaTicks > 0)
            {
                _lastGapMs = deltaTicks * 1000.0 / 1024.0 / deltaRevolutions;
                _lastChangeAt = now;
            }
        }
        else if (_lastRevolutions is null)
        {
            _lastChangeAt = now;
        }

        _lastRevolutions = revolutions;
        _lastEventTicks = eventTicks;

        reading = new CadenceReading(revolutions, CurrentRpm(now), Bounce: null, Suspect: null);
        return true;
    }

    /// <summary>
    /// As in the firmware: while no new revolution arrives the effective gap
    /// grows, so the reading decays smoothly instead of freezing at the last
    /// value.
    /// </summary>
    private double CurrentRpm(DateTimeOffset now)
    {
        if (_lastGapMs <= 0 || _lastChangeAt == default)
        {
            return 0;
        }

        var since = (now - _lastChangeAt).TotalMilliseconds;
        return since > IdleTimeout.TotalMilliseconds ? 0 : 60000.0 / Math.Max(_lastGapMs, since);
    }
}
