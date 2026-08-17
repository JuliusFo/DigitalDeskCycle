using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeskCycle.Desktop.Services;

public sealed class UserSettings
{
    /// <summary>Web server for external consumers. Off by default.</summary>
    public bool ApiEnabled { get; set; }

    public int ApiPort { get; set; } = 5056;

    /// <summary>
    /// false = localhost only. true binds to every address so that a phone on the
    /// same network can reach it -- at which point the Windows firewall asks.
    /// </summary>
    public bool ApiAllowRemote { get; set; }

    /// <summary>
    /// Body weight in kilograms, the basis for the calorie estimate. 0 means
    /// "not stated" -- then no figure is shown at all, rather than one for an
    /// invented default person.
    ///
    /// Belongs here rather than in appsettings.json: that one is part of the
    /// program and an update overwrites it.
    /// </summary>
    public double BodyWeightKg { get; set; }

    /// <summary>
    /// Start of the period the live view summarises.
    ///
    /// A reset only moves this timestamp -- nothing is deleted. That is why an
    /// accidental reset is harmless, and why correcting the conversion factor
    /// later applies here retroactively too.
    /// </summary>
    public DateTimeOffset? ResetAt { get; set; }
}

/// <summary>
/// Settings the user toggles at runtime -- kept apart from appsettings.json,
/// which belongs to the program and is overwritten by an update.
/// </summary>
public sealed class UserSettingsStore(ILogger<UserSettingsStore> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    public UserSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Current = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings();
            }
        }
        catch (Exception ex)
        {
            // A broken file must not prevent startup.
            logger.LogWarning(ex, "Settings could not be read, falling back to defaults.");
            Current = new UserSettings();
        }
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureDataDirectory();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, SerializerOptions));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Settings could not be saved.");
        }
    }
}
