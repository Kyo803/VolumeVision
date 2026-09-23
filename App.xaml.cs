using System.Threading;
using System.Windows;

namespace VolumeOSD;

public partial class App : Application
{
    private static Mutex? _mutex;

    internal static string CommandFile => System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "v2_cmd.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        Services.DebugLog.Write("App.OnStartup enter");

        // Second instance with a command flag: hand it to the running instance.
        string? cmd = null;
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) cmd = "settings";
        else if (e.Args.Contains("--studio", StringComparer.OrdinalIgnoreCase)) cmd = "studio";
        else if (e.Args.Contains("--library", StringComparer.OrdinalIgnoreCase)) cmd = "library";

        if (cmd != null)
        {
            try
            {
                System.IO.File.WriteAllText(CommandFile, cmd);
                Services.DebugLog.Write("wrote command flag: " + cmd);
            }
            catch (Exception ex) { Services.DebugLog.Write("flag write FAIL: " + ex.Message); }
            Shutdown();
            return;
        }

        // Single instance: second launch just exits
        _mutex = new Mutex(true, "V^2_SingleInstance", out bool created);
        if (!created)
        {
            Services.DebugLog.Write("App.OnStartup second instance -> exit");
            Shutdown();
            return;
        }

        Services.DebugLog.Write("App.OnStartup first instance, base.OnStartup");
        bool autostart = Services.AutostartHelper.EnsureEnabled();
        Services.DebugLog.Write($"autostart ensured: {autostart}");
        // Log UI-thread exceptions but keep the app alive (a settings-window bug
        // must never take the whole OSD down).
        DispatcherUnhandledException += (_, ex) =>
        {
            Services.DebugLog.Write("FATAL UI (handled, app kept alive): " + ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Services.DebugLog.Write("FATAL bg: " + ex.ExceptionObject);
        // Software rendering: this machine's GPU driver fails to composite
        // hardware-accelerated layered (AllowsTransparency) windows — they come
        // out blank. WPF's software rasterizer still does full anti-aliasing,
        // gradients and ClearType, so quality is unaffected here.
        System.Windows.Media.RenderOptions.ProcessRenderMode =
            System.Windows.Interop.RenderMode.SoftwareOnly;
        Services.DebugLog.Write("render mode = SoftwareOnly (layered-window safe)");
        base.OnStartup(e);

        MainWindow? mainWin = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Services.DebugLog.Write($"creating MainWindow manually (attempt {attempt})");
                mainWin = new MainWindow();
                Services.DebugLog.Write("MainWindow created, calling Show()");
                MainWindow = mainWin;
                mainWin.Show();
                Services.DebugLog.Write($"Show() returned, Visibility={mainWin.Visibility}");
                break;
            }
            catch (Exception ex)
            {
                Services.DebugLog.Write($"MANUAL SHOW FAIL (attempt {attempt}): {ex}");
                if (attempt == 3) break;
                Thread.Sleep(10000);
            }
        }

        // Poll for a command flag from a second instance (every 2s).
        if (mainWin != null)
        {
            var pollTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(2) };
            pollTimer.Tick += (_, _) =>
            {
                try
                {
                    if (!System.IO.File.Exists(CommandFile)) return;
                    string cmd = System.IO.File.ReadAllText(CommandFile).Trim().ToLowerInvariant();
                    System.IO.File.Delete(CommandFile);
                    Services.DebugLog.Write("command consumed: " + cmd);
                    switch (cmd)
                    {
                        case "settings": mainWin.OpenSettings(); break;
                        case "studio": mainWin.OpenStudio(); break;
                        case "library": mainWin.OpenLibrary(); break;
                    }
                }
                catch { }
            };
            pollTimer.Start();
        }
    }
}
