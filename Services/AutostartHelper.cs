using Microsoft.Win32;

namespace VolumeOSD.Services;

/// <summary>Login autostart. Prefers machine-wide (HKLM, all users) when
/// elevated, falls back to per-user (HKCU). Also cleans the pre-rename
/// "VolumeOSD" value on sight.</summary>
public static class AutostartHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "V^2";
    private const string LegacyValueName = "VolumeOSD";

    private static string? ExePath()
    {
        string? exe = Environment.ProcessPath;
        // Only register the real exe (never `dotnet.exe` from `dotnet run`).
        if (string.IsNullOrEmpty(exe) || !exe.EndsWith("V^2.exe", StringComparison.OrdinalIgnoreCase))
            return null;
        return exe;
    }

    private static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static bool EnsureEnabled()
    {
        try
        {
            string? exe = ExePath();
            if (exe == null) return false;
            string want = "\"" + exe + "\"";
            // Machine-wide first when we can, per-user otherwise.
            bool machine = false;
            if (IsElevated())
            {
                try
                {
                    using var lm = Registry.LocalMachine.OpenSubKey(RunKey, writable: true);
                    if (lm != null)
                    {
                        string cur = (lm.GetValue(ValueName) as string) ?? "";
                        if (!string.Equals(cur, want, StringComparison.OrdinalIgnoreCase))
                            lm.SetValue(ValueName, want);
                        machine = true;
                    }
                }
                catch { }
            }
            using var cu = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (cu != null)
            {
                // Drop the pre-rename leftover.
                try { cu.DeleteValue(LegacyValueName, throwOnMissingValue: false); } catch { }
                if (!machine)
                {
                    string cur = (cu.GetValue(ValueName) as string) ?? "";
                    if (!string.Equals(cur, want, StringComparison.OrdinalIgnoreCase))
                        cu.SetValue(ValueName, want);
                }
            }
            return true;
        }
        catch { return false; }
    }

    public static bool IsEnabled()
    {
        try
        {
            string? exe = ExePath();
            if (exe == null) return false;
            string want = "\"" + exe + "\"";
            using var lm = Registry.LocalMachine.OpenSubKey(RunKey, writable: false);
            if (string.Equals(lm?.GetValue(ValueName) as string, want, StringComparison.OrdinalIgnoreCase))
                return true;
            using var cu = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return string.Equals(cu?.GetValue(ValueName) as string, want, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void Disable()
    {
        try
        {
            try
            {
                using var lm = Registry.LocalMachine.OpenSubKey(RunKey, writable: true);
                lm?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            catch { /* non-elevated: HKLM is read-only for us */ }
            using var cu = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            cu?.DeleteValue(ValueName, throwOnMissingValue: false);
            cu?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        catch { }
    }
}
