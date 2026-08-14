using System.IO.Ports;
using System.Text;
using DeskCycle.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeskCycle.Core.Tracking;

/// <summary>
/// Reads the line-based output of the Pico firmware from the COM port.
///
/// The preferred source: only here do the diagnostic counters <c>bounce</c> and
/// <c>suspect</c> come along, which Bluetooth cannot carry by design.
/// </summary>
public sealed class SerialCadenceSource(
    SessionRecorder recorder,
    IOptions<TrackingOptions> options,
    ILogger<SerialCadenceSource> logger) : ICadenceSource
{
    private readonly TrackingOptions _options = options.Value;

    /// <summary>Keeps the same message from flooding the log every few seconds.</summary>
    private string? _lastPortProblem;

    public string Name => "USB";

    public async Task<bool> TryRunAsync(CancellationToken cancellationToken)
    {
        var portName = ResolvePortName();
        if (portName is null)
        {
            return false;
        }

        using var port = new SerialPort(portName, _options.BaudRate, Parity.None, 8, StopBits.One)
        {
            NewLine = "\n",
            ReadTimeout = 5000,
            DtrEnable = true,
        };

        try
        {
            port.Open();
        }
        catch (Exception ex)
        {
            // Port busy (Thonny, for instance) or gone: that is "unavailable",
            // not "broke off". Only this way does the coordinator ever reach the
            // next source.
            logger.LogInformation(
                "{Port} cannot be opened ({Reason}). Trying the next source.",
                portName, ex.Message);
            return false;
        }

        logger.LogInformation("Sensor connected on {Port}.", portName);

        // The Pico's counter runs regardless of us. After a break it is unknown
        // how many revolutions accumulated in the meantime -- so re-establish a
        // reference point instead of guessing.
        recorder.OnSourceConnected();

        using var reader = new StreamReader(port.BaseStream, Encoding.ASCII);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (CadenceLineParser.TryParseCad(line.Trim(), out var reading))
            {
                await recorder.OnReadingAsync(reading, portName, cancellationToken);
            }
        }

        return true;
    }

    private string? ResolvePortName()
    {
        if (!string.IsNullOrWhiteSpace(_options.SerialPort))
        {
            return _options.SerialPort;
        }

        var ports = SerialPort.GetPortNames();

        if (ports.Length == 1)
        {
            _lastPortProblem = null;
            return ports[0];
        }

        // Several ports are the norm on Windows as soon as Bluetooth is involved
        // -- rather than guessing, let it be configured.
        var problem = ports.Length == 0
            ? "No COM port found."
            : $"Several COM ports found ({string.Join(", ", ports)}). Please set Tracking:SerialPort.";

        if (problem != _lastPortProblem)
        {
            logger.LogInformation("{Problem} Trying the next source.", problem);
            _lastPortProblem = problem;
        }

        return null;
    }
}
