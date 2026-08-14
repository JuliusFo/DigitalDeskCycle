using System.Text;
using Microsoft.Extensions.Logging;

namespace DeskCycle.Desktop.Logging;

/// <summary>
/// Minimal file logger.
///
/// A windowed application has no console: without this provider every message
/// from the host, the serial port and the database vanishes without trace.
/// Deliberately hand-written rather than pulled in as a library -- what is
/// needed is a timestamp, a level, a category and the text, and nothing more.
/// </summary>
public sealed class FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information)
    : ILoggerProvider
{
    private readonly FileLogWriter _writer = new(directory);

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, _writer, minimumLevel);

    public void Dispose() => _writer.Dispose();

    private sealed class FileLogger(string category, FileLogWriter writer, LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var builder = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(' ')
                .Append(Abbreviate(logLevel))
                .Append(' ')
                .Append(ShortCategory(category))
                .Append(" | ")
                .Append(formatter(state, exception));

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            writer.Write(builder.ToString());
        }

        private static string Abbreviate(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };

        /// <summary>Class name only; the full namespace bloats every line.</summary>
        private static string ShortCategory(string category)
        {
            var lastDot = category.LastIndexOf('.');
            return lastDot >= 0 && lastDot < category.Length - 1
                ? category[(lastDot + 1)..]
                : category;
        }
    }
}
