using System.Diagnostics;
using System.IO;
using System.Windows;

namespace VolumeOSD.Services;

/// <summary>
/// Full self-removal: autostart value, settings, shortcuts, then the exe itself.
/// Safe to call from tray or settings; always asks first and logs each step.
/// </summary>
public static class Uninstaller
{
    public static void RunUninstall(Window? owner)
    {
        var res = MessageBox.Show(owner,
            "Remove VolumeOSD?\n\nThis deletes the login autostart entry, settings, " +
            "shortcuts and the application file itself, then exits.",
            "Uninstall VolumeOSD",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (res != MessageBoxResult.Yes) return;

        DebugLog.Write("uninstall confirmed by user");

        // 1. Login autostart
        try { AutostartHelper.Disable(); DebugLog.Write("uninstall: autostart removed"); }
        catch (Exception ex) { DebugLog.Write("uninstall autostart FAIL: " + ex.Message); }

        // 2. Settings folder
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VolumeOSD");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            DebugLog.Write("uninstall: settings removed");
        }
        catch (Exception ex) { DebugLog.Write("uninstall settings FAIL: " + ex.Message); }

        // 3. Shortcuts (dev shortcut + installer group + desktop)
        try
        {
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string[] paths =
            {
                Path.Combine(startMenu, "VolumeOSD.lnk"),
                Path.Combine(desktop, "VolumeOSD.lnk"),
            };
            foreach (var p in paths)
                if (File.Exists(p)) File.Delete(p);
            string group = Path.Combine(startMenu, "VolumeOSD");
            if (Directory.Exists(group)) Directory.Delete(group, recursive: true);
            DebugLog.Write("uninstall: shortcuts removed");
        }
        catch (Exception ex) { DebugLog.Write("uninstall shortcuts FAIL: " + ex.Message); }

        // 4. The exe itself, deleted by a detached cmd after we exit
        //    (a running file can't delete itself). Containing folder is left alone.
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 /nobreak >nul & del /f /q \"{exe}\"")
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                });
                DebugLog.Write("uninstall: self-delete scheduled for " + exe);
            }
        }
        catch (Exception ex) { DebugLog.Write("uninstall self-delete FAIL: " + ex.Message); }

        Application.Current.Shutdown();
    }
}
