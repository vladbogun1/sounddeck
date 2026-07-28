using Microsoft.Win32;

namespace SoundDeck.Storage;

// Toggles launch-on-login via the HKCU Run key.
public static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "SoundDeck";

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) != null;
        }
        catch { return false; }
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(Key);
            if (on)
            {
                string exe = Environment.ProcessPath ?? "";
                // Launch-on-login starts silently in the tray; a manual double-click
                // (no args) opens the window.
                if (!string.IsNullOrEmpty(exe)) k?.SetValue(Name, $"\"{exe}\" --minimized");
            }
            else k?.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch { }
    }
}
