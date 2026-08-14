using System.IO;
using System.Text;

namespace DeskCycle.Desktop.Logging;

/// <summary>
/// Writes log lines into one file per day and cleans up old ones.
///
/// The file is shared for reading (FileShare.ReadWrite) so that it can be
/// followed while the application runs, in an editor or with
/// "Get-Content -Wait".
/// </summary>
internal sealed class FileLogWriter(string directory, int retentionDays = 14) : IDisposable
{
    private readonly object _gate = new();

    private StreamWriter? _writer;
    private DateOnly _currentDay;

    public void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);

                if (_writer is null || today != _currentDay)
                {
                    Roll(today);
                }

                _writer!.WriteLine(line);
            }
            catch (Exception)
            {
                // Logging must never bring the application down -- a full disk is
                // no reason to stop recording.
            }
        }
    }

    private void Roll(DateOnly day)
    {
        _writer?.Dispose();
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"deskcycle-{day:yyyy-MM-dd}.log");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        _currentDay = day;

        DeleteExpired();
    }

    private void DeleteExpired()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-retentionDays);

            foreach (var file in Directory.EnumerateFiles(directory, "deskcycle-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception)
        {
            // Cleaning up is a nicety; if it fails, the folder just grows.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
