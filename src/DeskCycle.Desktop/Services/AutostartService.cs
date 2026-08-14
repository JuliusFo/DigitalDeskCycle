using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DeskCycle.Desktop.Services;

/// <summary>
/// Autostart through HKCU\...\Run.
///
/// Deliberately the per-user key rather than HKLM: no administrator rights are
/// needed, and the entry can be switched off again through Task Manager without
/// ever starting this application.
/// </summary>
public sealed class AutostartService(ILogger<AutostartService> logger)
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Shows up under this name in Task Manager's startup list.</summary>
    private const string ValueName = "DigitalDeskCycle";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public void SetEnabled(bool enabled)
    {
        var executable = Environment.ProcessPath;

        if (enabled && string.IsNullOrEmpty(executable))
        {
            logger.LogWarning("Cannot determine the application path, autostart not set up.");
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{executable}\"");
                logger.LogInformation("Autostart enabled.");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                logger.LogInformation("Autostart removed.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not change the autostart entry.");
        }
    }
}
