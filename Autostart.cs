using Microsoft.Win32;

namespace Skuload.PrintBridge;

/// <summary>
/// Toggles "launch at login" via the per-user Run key. No admin needed — it
/// only touches HKCU.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "skuload-print-bridge";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex)
        {
            Logger.Error("Autostart read failed", ex);
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExePath()}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Autostart write failed", ex);
        }
    }

    private static string ExePath() => Environment.ProcessPath ?? Application.ExecutablePath;
}
