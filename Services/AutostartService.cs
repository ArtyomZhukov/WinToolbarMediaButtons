using Microsoft.Win32;

namespace WinToolbarMediaButtons.Services;

static class AutostartService
{
    private const string ValueName = "WinToolbarMediaButtons";
    private const string RegPath   = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        return key?.GetValue(ValueName) is string path && path == Environment.ProcessPath;
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath, writable: true)!;
        key.SetValue(ValueName, Environment.ProcessPath!);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath, writable: true)!;
        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
