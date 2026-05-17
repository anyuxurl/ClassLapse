using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ClassLapse.Core;

/// <summary>
/// Manages the HKCU\...\Run entry that auto-starts ClassLapse with Windows.
/// Uses HKCU (not HKLM) so no admin rights are required. The exe path is
/// wrapped in quotes if it contains spaces — required so Windows passes
/// the whole path as argv[0] rather than splitting on the first space.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClassLapse";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static string? CurrentTargetPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) as string;
        }
        catch
        {
            return null;
        }
    }

    public static void Enable(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("exePath must not be empty", nameof(exePath));

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        string value = exePath.Contains(' ') ? $"\"{exePath}\"" : exePath;
        key.SetValue(ValueName, value);
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // best-effort
        }
    }
}
