using System.IO;

namespace DeskCycle.Desktop.Services;

/// <summary>
/// Where the training data lives.
///
/// Deliberately not next to the executable: a rebuild, a moved program folder or
/// a deleted bin directory must not take the history with it.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeskCycle");

    public static string DatabaseFile => Path.Combine(DataDirectory, "deskcycle.db");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string ConnectionString => $"Data Source={DatabaseFile}";

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}
