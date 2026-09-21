using System.Threading;
using System.Windows;

namespace VolumeOSD;

public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        Services.DebugLog.Write("App.OnStartup enter");
        // Single instance: second launch just exits (prevents double volume steps + double pills)
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
        DispatcherUnhandledException += (_, e) =>
            Services.DebugLog.Write("FATAL UI: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Services.DebugLog.Write("FATAL bg: " + e.ExceptionObject);
        // Force software rendering: on some GPU drivers the hardware pipeline
        // presents blank layered windows (alive UI, input works, zero pixels).
        // A 300px OSD costs nothing to rasterize on CPU.
        System.Windows.Media.RenderOptions.ProcessRenderMode =
            System.Windows.Interop.RenderMode.SoftwareOnly;
        Services.DebugLog.Write("render mode = SoftwareOnly");
        base.OnStartup(e);
        // If construction fails (e.g. drivers still loading at boot), retry
        // instead of running headless with no window.
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Services.DebugLog.Write($"creating MainWindow manually (attempt {attempt})");
                var w = new MainWindow();
                Services.DebugLog.Write("MainWindow created, calling Show()");
                MainWindow = w;
                w.Show();
                Services.DebugLog.Write($"Show() returned, Visibility={w.Visibility}");
                break;
            }
            catch (Exception ex)
            {
                Services.DebugLog.Write($"MANUAL SHOW FAIL (attempt {attempt}): {ex}");
                if (attempt == 3) break;
                Thread.Sleep(10000);
            }
        }
    }
}
