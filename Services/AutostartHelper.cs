using Microsoft.Win32;

namespace VolumeOSD.Services;

/// <summary>Registers the exe under HKCU\...\Run so the OSD survives reboots.</summary>
public static class AutostartHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VolumeOSD";

    public static bool EnsureEnabled()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            // Only register the real exe (never `dotnet.exe` from `dotnet run`).
            if (string.IsNullOrEmpty(exe) || !exe.EndsWith("VolumeOSD.exe", StringComparison.OrdinalIgnoreCase))
                return false;
            string want = "\"" + exe + "\"";
            using var rk = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (rk == null) return false;
            string cur = (rk.GetValue(ValueName) as string) ?? "";
            if (!string.Equals(cur, want, StringComparison.OrdinalIgnoreCase))
                rk.SetValue(ValueName, want);
            return true;
        }
        catch { return false; }
    }

    public static bool IsEnabled()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !exe.EndsWith("VolumeOSD.exe", StringComparison.OrdinalIgnoreCase))
                return false;
            string want = "\"" + exe + "\"";
            using var rk = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            string cur = (rk?.GetValue(ValueName) as string) ?? "";
            return string.Equals(cur, want, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void Disable()
    {
        try
        {
            using var rk = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            rk?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { }
    }
}
