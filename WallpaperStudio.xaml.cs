using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KGySoft.Drawing;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;

namespace VolumeOSD;

/// <summary>
/// Wallpaper studio: crop/pan/zoom + adjustments with a live pill preview,
/// Ken Burns animation preview, PNG snapshot or animated-GIF export straight
/// into the pill wallpaper slot.
/// </summary>
public partial class WallpaperStudio : Window
{
    private const double VpW = 480, VpH = 106; // crop viewport = pill aspect

    private readonly MainWindow _main;
    private SD.Bitmap? _source;
    private double _zoom = 1.0; // >= 1 (cover)
    private double _cx;         // crop center in source px
    private double _cy;
    private bool _dragging;
    private Point _dragLast;
    private bool _ready;
    private readonly DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private double _animT;
    private bool _exporting;

    public WallpaperStudio(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        _animTimer.Tick += (_, _) => AnimTick();
        _ready = true;
        UpdateFrameInfo();
    }

    // ---------- Source ----------

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
            Title = "Open art for the wallpaper",
        };
        if (dlg.ShowDialog() != true) return;
        LoadFile(dlg.FileName);
    }

    public bool LoadFile(string path)
    {
        try
        {
            _source?.Dispose();
            _source = (SD.Bitmap)SD.Image.FromFile(path);
            _zoom = 1.0;
            _cx = _source.Width / 2.0;
            _cy = _source.Height / 2.0;
            ZoomSlider.Value = 100;
            StatusText.Text = System.IO.Path.GetFileName(path);
            UpdateFrameInfo();
            RenderView();
            return true;
        }
        catch (Exception ex) { StatusText.Text = "Could not open: " + ex.Message; return false; }
    }

    private double BaseScale()
    {
        if (_source == null) return 1;
        return Math.Max(VpW / _source.Width, VpH / _source.Height);
    }

    private void ClampCenter(ref double cx, ref double cy, double zoom)
    {
        if (_source == null) return;
        double b = BaseScale();
        double cw = Math.Min(VpW / (b * zoom), _source.Width);
        double ch = Math.Min(VpH / (b * zoom), _source.Height);
        cx = Math.Clamp(cx, cw / 2, _source.Width - cw / 2);
        cy = Math.Clamp(cy, ch / 2, _source.Height - ch / 2);
        if (_source.Width <= cw) cx = _source.Width / 2;
        if (_source.Height <= ch) cy = _source.Height / 2;
    }

    // ---------- Crop pan/zoom ----------

    private void Crop_Down(object sender, MouseButtonEventArgs e)
    {
        if (_source == null) return;
        CropImage.CaptureMouse();
        _dragging = true;
        _dragLast = e.GetPosition(CropImage);
    }

    private void Crop_Move(object sender, MouseEventArgs e)
    {
        if (!_dragging || !CropImage.IsMouseCaptured || _source == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CropImage.ReleaseMouseCapture(); _dragging = false; return; }
        var p = e.GetPosition(CropImage);
        double s = BaseScale() * _zoom;
        // Viewport bitmap is VpW wide but displayed at CropImage width (same here).
        double k = VpW / CropImage.ActualWidth;
        _cx -= (p.X - _dragLast.X) * k / s;
        _cy -= (p.Y - _dragLast.Y) * k / s;
        _dragLast = p;
        RenderView();
    }

    private void Crop_Up(object sender, MouseButtonEventArgs e)
    {
        CropImage.ReleaseMouseCapture();
        _dragging = false;
    }

    // ---------- Controls ----------

    private void Adjust_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        ZoomVal.Text = $"{ZoomSlider.Value:F0}%";
        BrightVal.Text = $"{BrightSlider.Value:F0}";
        ContrastVal.Text = $"{ContrastSlider.Value:F0}%";
        SatVal.Text = $"{SatSlider.Value:F0}%";
        _zoom = Math.Clamp(ZoomSlider.Value / 100.0, 1.0, 4.0);
        RenderView();
    }

    private void Adjust_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        RenderView();
    }

    private void Kb_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateFrameInfo();
        RenderView();
    }

    private void Anim_Toggled(object sender, RoutedEventArgs e)
    {
        _animT = 0;
        if (AnimCheck.IsChecked == true && _source != null && KbCombo.SelectedIndex > 0)
            _animTimer.Start();
        else
        {
            _animTimer.Stop();
            RenderView();
        }
    }

    private void UpdateFrameInfo()
    {
        if (_source == null) { FrameInfo.Text = "No image loaded"; return; }
        if (KbCombo.SelectedIndex <= 0) { FrameInfo.Text = "Still image — PNG snapshot available"; return; }
        int fps = Fps(), n = (int)Math.Round(fps * Duration());
        FrameInfo.Text = $"{n} frames · {Duration():F0}s @ {fps}fps · 436×96 GIF on export";
    }

    private int Fps() => int.TryParse(((ComboBoxItem)FpsCombo.SelectedItem)?.Tag?.ToString(), out int f) ? f : 12;
    private double Duration() => double.TryParse(((ComboBoxItem)DurCombo.SelectedItem)?.Tag?.ToString(), out double d) ? d : 3;

    // ---------- Render ----------

    private static float[][] Mul5(float[][] a, float[][] b)
    {
        var r = new float[5][];
        for (int i = 0; i < 5; i++)
        {
            r[i] = new float[5];
            for (int j = 0; j < 5; j++)
            {
                float s = 0;
                for (int k = 0; k < 5; k++) s += a[i][k] * b[k][j];
                r[i][j] = s;
            }
        }
        return r;
    }

    private float[][] AdjustMatrix(AdjustState a)
    {
        float bright = a.Bright, contrast = a.Contrast, sat = a.Gray ? 0 : a.Sat;
        const float lr = 0.2126f, lg = 0.7152f, lb = 0.0722f;
        float[][] s = {
            new float[] { (1 - sat) * lr + sat, (1 - sat) * lg, (1 - sat) * lb, 0, 0 },
            new float[] { (1 - sat) * lr, (1 - sat) * lg + sat, (1 - sat) * lb, 0, 0 },
            new float[] { (1 - sat) * lr, (1 - sat) * lg, (1 - sat) * lb + sat, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 0, 0, 1 },
        };
        float co = 0.5f * (1 - contrast);
        float[][] c = {
            new float[] { contrast, 0, 0, 0, 0 },
            new float[] { 0, contrast, 0, 0, 0 },
            new float[] { 0, 0, contrast, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { co + bright, co + bright, co + bright, 0, 1 },
        };
        return Mul5(s, c);
    }

    private record AdjustState(float Bright, float Contrast, float Sat, bool Gray);

    private AdjustState ReadAdjust() => new(
        (float)(BrightSlider.Value / 100.0),
        (float)(ContrastSlider.Value / 100.0),
        (float)(SatSlider.Value / 100.0),
        GrayCheck.IsChecked == true);

    private SD.Bitmap? RenderFrame(int outW, int outH, double zoom, double cx, double cy, AdjustState adj)
    {
        if (_source == null) return null;
        double b = BaseScale();
        double cw = Math.Min(VpW / (b * zoom), _source.Width);
        double ch = Math.Min(VpH / (b * zoom), _source.Height);
        // (cx,cy) is the crop CENTER (pre-clamped) — top-left = center minus half size.
        double x = cx - cw / 2, y = cy - ch / 2;
        x = Math.Clamp(x, 0, Math.Max(0, _source.Width - cw));
        y = Math.Clamp(y, 0, Math.Max(0, _source.Height - ch));
        var bmp = new SD.Bitmap(outW, outH, SDI.PixelFormat.Format24bppRgb);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            using var attrs = new SDI.ImageAttributes();
            attrs.SetColorMatrix(new SDI.ColorMatrix(AdjustMatrix(adj)));
            g.DrawImage(_source,
                new SD.Rectangle(0, 0, outW, outH),
                (float)x, (float)y, (float)cw, (float)ch,
                SD.GraphicsUnit.Pixel, attrs);
        }
        return bmp;
    }

    private void RenderView() => RenderView(_zoom, _cx, _cy);

    private void RenderView(double zoom, double cx, double cy)
    {
        if (!_ready || _source == null) return;
        ClampCenter(ref cx, ref cy, zoom);
        if (!_animTimer.IsEnabled) { _cx = cx; _cy = cy; }
        using var bmp = RenderFrame(480, 106, zoom, cx, cy, ReadAdjust());
        if (bmp == null) return;
        PreviewImage.Source = ToBitmapImage(bmp);
        CropImage.Source = PreviewImage.Source;
    }

    private static BitmapImage ToBitmapImage(SD.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, SDI.ImageFormat.Png);
        ms.Seek(0, SeekOrigin.Begin);
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    // ---------- Ken Burns ----------

    private void AnimTick()
    {
        if (_source == null) return;
        _animT += 0.033 / Duration();
        if (_animT > 1) _animT -= 1;
        double p = _animT, z = _zoom, cx = _cx, cy = _cy;
        double b = BaseScale();
        switch (KbCombo.SelectedIndex)
        {
            case 1: z = _zoom * (1 + 0.3 * p); break;                    // zoom in
            case 2: z = _zoom * (1.3 - 0.3 * p); break;                  // zoom out
            case 3: // pan sideways (ensure slack, sweep center)
                z = Math.Max(_zoom, 1.15);
                double cw = Math.Min(VpW / (b * z), _source.Width);
                double lo = cw / 2, hi = _source.Width - cw / 2;
                cx = lo + (hi - lo) * p;
                break;
        }
        RenderView(z, cx, cy);
    }

    // ---------- Export ----------

    private void Png_Click(object sender, RoutedEventArgs e)
    {
        if (_source == null || _exporting) return;
        try
        {
            using var bmp = RenderFrame(872, 192, _zoom, _cx, _cy, ReadAdjust());
            if (bmp == null) return;
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "volwall.png");
            bmp.Save(tmp, SDI.ImageFormat.Png);
            _main.ImportWallpaperFile(tmp, ".png");
            try { System.IO.File.Delete(tmp); } catch { }
            StatusText.Text = "PNG wallpaper applied to the pill.";
        }
        catch (Exception ex) { StatusText.Text = "PNG failed: " + ex.Message; }
    }

    private async void Gif_Click(object sender, RoutedEventArgs e)
    {
        if (_source == null || _exporting) return;
        if (KbCombo.SelectedIndex <= 0)
        {
            StatusText.Text = "Pick an animation mode first (zoom/pan).";
            return;
        }
        _exporting = true;
        try
        {
            int fps = Fps(), n = Math.Max(2, (int)Math.Round(fps * Duration()));
            int kbMode = KbCombo.SelectedIndex;
            double z0 = _zoom, cx0 = _cx, cy0 = _cy;
            var adj = ReadAdjust();
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "volwall.gif");
            await Task.Run(() =>
            {
                var frames = new List<SD.Bitmap>(n);
                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        double p = n == 1 ? 0 : (double)i / (n - 1);
                        double z = z0, cx = cx0, cy = cy0;
                        double b = BaseScale();
                        switch (kbMode)
                        {
                            case 1: z = z0 * (1 + 0.3 * p); break;
                            case 2: z = z0 * (1.3 - 0.3 * p); break;
                            case 3:
                                z = Math.Max(z0, 1.15);
                                double cw = Math.Min(VpW / (b * z), _source.Width);
                                double lo = cw / 2, hi = _source.Width - cw / 2;
                                cx = lo + (hi - lo) * p;
                                break;
                        }
                        var f = RenderFrame(436, 96, z, cx, cy, adj);
                        if (f != null) frames.Add(f);
                        int done = i + 1;
                        if (done % 6 == 0 || done == n)
                            Dispatcher.Invoke(() => StatusText.Text = $"Rendering {done}/{n}…");
                    }
                    frames.SaveAsAnimatedGif(tmp, TimeSpan.FromMilliseconds(1000.0 / fps), null, null);
                    Dispatcher.Invoke(() =>
                    {
                        _main.ImportWallpaperFile(tmp, ".gif");
                        StatusText.Text = $"GIF applied ({n} frames).";
                    });
                }
                finally
                {
                    foreach (var f in frames) f.Dispose();
                    try { System.IO.File.Delete(tmp); } catch { }
                }
            });
        }
        catch (Exception ex) { StatusText.Text = "GIF failed: " + ex.Message; }
        finally
        {
            _exporting = false;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _animTimer.Stop();
        _source?.Dispose();
        _source = null;
        base.OnClosed(e);
    }
}
