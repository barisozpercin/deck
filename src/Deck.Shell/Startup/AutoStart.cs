using Microsoft.Win32;

namespace Deck.Shell.Startup;

/// <summary>
/// Registers the deck to launch with Windows. Deliberately re-asserted on every start so the
/// entry self-heals when the executable moves — a stale path here fails silently at boot, which
/// is the worst way for it to fail.
/// </summary>
internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Deck";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Not worth interrupting startup over; the tray toggle shows the real state.
        }
    }

    /// <summary>Refreshes the stored path if autostart is already on. Never turns it on.</summary>
    public static void RefreshIfEnabled()
    {
        if (IsEnabled) Set(true);
    }
}
