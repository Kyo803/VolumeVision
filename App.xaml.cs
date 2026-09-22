using System.Threading;
using System.Windows;

namespace VolumeOSD;

public partial class App : Application
{
    private static Mutex? _mutex;
    internal static bool OpenSettingsRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        Services.DebugLog.Write("App.OnStartup enter");
        bool openSettings = e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase);

        if (openSettings)
        {
            // Second instance with --settings: tell the first instance via a named file, then exit.
            try
            {
                string flag = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "v2_settings.open");
                System.IO.File.WriteAllText(flag, DateTime.UtcNow.ToString("O"));
                Services.DebugLog.Write("wrote settings flag, exiting");
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
        DispatcherUnhandledException += (_, ex) =>
            Services.DebugLog.Write("FATAL UI: " + ex.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Services.DebugLog.Write("FATAL bg: " + ex.ExceptionObject);
        System.Windows.Media.RenderOptions.ProcessRenderMode =
            System.Windows.Interop.RenderMode.SoftwareOnly;
        Services.DebugLog.Write("render mode = SoftwareOnly");
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

        // Poll for --settings flag from a second instance (checks every 2s).
        if (mainWin != null)
        {
            var pollTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(2) };
            pollTimer.Tick += (_, _) =>
            {
                try
                {
                    string flag = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "v2_settings.open");
                    if (System.IO.File.Exists(flag))
                    {
                        System.IO.File.Delete(flag);
                        mainWin.OpenSettings();
                        Services.DebugLog.Write("settings flag consumed, opening settings");
                    }
                }
                catch { }
            };
            pollTimer.Start();
        }
    }
}
