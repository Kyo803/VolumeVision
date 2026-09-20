using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace VolumeOSD.Services;

/// <summary>
/// System-tray identity for the app: summon/settings/autostart/exit.
/// Icon is drawn in code (dark disc + green ring + play wedge), no asset files.
/// </summary>
public sealed class VolumeTray : IDisposable
{
    private readonly MainWindow _main;
    private readonly WinForms.NotifyIcon _icon;
    private readonly IntPtr _hIcon;
    private bool _disposed;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public VolumeTray(MainWindow main)
    {
        _main = main;
        _hIcon = BuildIconHandle();
        _icon = new WinForms.NotifyIcon
        {
            Icon = Icon.FromHandle(_hIcon),
            Text = "VolumeOSD — custom volume bar",
            Visible = true,
        };

        var show = new WinForms.ToolStripMenuItem("Show pill", null, (_, _) => _main.Dispatcher.Invoke(_main.Summon));
        var settings = new WinForms.ToolStripMenuItem("Settings…", null, (_, _) => _main.Dispatcher.Invoke(_main.OpenSettings));
        var autostart = new WinForms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        autostart.Click += (_, _) =>
        {
            if (autostart.Checked) AutostartHelper.EnsureEnabled();
            else AutostartHelper.Disable();
        };
        var exit = new WinForms.ToolStripMenuItem("Exit", null, (_, _) => _main.Dispatcher.Invoke(() => Application.Current.Shutdown()));

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(show);
        menu.Items.Add(settings);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(autostart);
        menu.Items.Add(exit);
        menu.Opening += (_, _) => autostart.Checked = AutostartHelper.IsEnabled();
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => _main.Dispatcher.Invoke(_main.Summon);
    }

    private static IntPtr BuildIconHandle()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var bg = new SolidBrush(Color.FromArgb(255, 17, 17, 17)))
                g.FillEllipse(bg, 2, 2, 60, 60);
            using (var ring = new Pen(Color.FromArgb(255, 45, 166, 59), 6))
                g.DrawEllipse(ring, 8, 8, 48, 48);
            using (var play = new SolidBrush(Color.White))
                g.FillPolygon(play, new[] { new System.Drawing.Point(27, 20), new System.Drawing.Point(27, 44), new System.Drawing.Point(45, 32) });
        }
        return bmp.GetHicon();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _icon.Visible = false; _icon.Dispose(); } catch { }
        try { if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon); } catch { }
    }
}
