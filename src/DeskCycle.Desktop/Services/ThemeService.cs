using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DeskCycle.Desktop.Services;

/// <summary>
/// Light or dark, following the Windows setting.
///
/// At startup the registry value Windows itself keeps for "app mode" is read.
/// Later changes are reported by the SystemThemeWatcher that the window
/// registers -- a second watcher of our own would be redundant.
/// </summary>
public static class ThemeService
{
    /// <summary>Mica: the milky window backdrop of Windows 11.</summary>
    public const WindowBackdropType Backdrop = WindowBackdropType.Mica;

    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

    public static void ApplySystemTheme() =>
        ApplicationThemeManager.Apply(
            SystemUsesDarkTheme() ? ApplicationTheme.Dark : ApplicationTheme.Light,
            Backdrop,
            true);

    /// <summary>
    /// AppsUseLightTheme is the value Windows keeps for applications -- separate
    /// from SystemUsesLightTheme, which only concerns the taskbar and the start
    /// menu. If the value is missing, light applies.
    /// </summary>
    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int useLight && useLight == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
