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
    private readonly List<SD.Bitmap> _frames = new();
    private int _frameIndex;
    private readonly System.Collections.ObjectModel.ObservableCollection<Controls.TimelineItem> _items = new();
    private readonly List<(int Index, SD.Bitmap Snap)> _undo = new();
    private double _zoom = 1.0; // >= 1 (cover)
    private double _cx;         // crop center in source px
    private double _cy;
    private bool _dragging;
    private Point _dragLast;
    private bool _ready;
    private readonly DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _playTimer = new() { Interval = TimeSpan.FromMilliseconds(83) };
    private bool _playing;
    private double _animT;
    private bool _exporting;
    private System.Windows.Media.Color _penColor = Colors.White;
    private SD.Bitmap? _brushTip;    // raw tip (null = round pen)
    private SD.Bitmap? _tintedTip;   // tip recolored to pen color
    private double _brushSpacing = 0.22;

    private SD.Bitmap? Current => _frames.Count == 0 ? null : _frames[Math.Clamp(_frameIndex, 0, _frames.Count - 1)];

    public WallpaperStudio(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        Timeline.ItemsSource = _items;
        Timeline.FrameSelected += Timeline_FrameSelected;
        Timeline.AddRequested += () => FrameAdd_Click(this, new RoutedEventArgs());
        Timeline.DuplicateRequested += () => FrameDupe_Click(this, new RoutedEventArgs());
        Timeline.DeleteRequested += () => FrameDel_Click(this, new RoutedEventArgs());
        Timeline.UndoRequested += () => Undo_Click(this, new RoutedEventArgs());
        Timeline.PlayToggled += TogglePlay;
        Timeline.StopRequested += StopPlay;
        Timeline.StepBy += StepBy;
        PenColorBtn.Background = new SolidColorBrush(_penColor);
        _animTimer.Tick += (_, _) => AnimTick();
        _playTimer.Tick += (_, _) => PlayTick();
        _ready = true;
        LoadBrushes();
        UpdateFrameInfo();

        // Smooth entrance: fade + subtle scale-up.
        Opacity = 0;
        ShellScale.ScaleX = ShellScale.ScaleY = 0.96;
        Loaded += (_, _) =>
        {
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            var grow = new System.Windows.Media.Animation.DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
            ShellScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            ShellScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        };
    }

    private void Shell_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private static SD.Bitmap To32bpp(SD.Image src)
    {
        var bmp = new SD.Bitmap(src.Width, src.Height, SDI.PixelFormat.Format32bppArgb);
        using var g = SD.Graphics.FromImage(bmp);
        g.DrawImage(src, 0, 0, src.Width, src.Height);
        return bmp;
    }

    private void ResetView()
    {
        var cur = Current;
        _zoom = 1.0;
        _cx = (cur?.Width ?? 2) / 2.0;
        _cy = (cur?.Height ?? 2) / 2.0;
        ZoomSlider.Value = 100;
    }

    private void RebuildThumbs()
    {
        _items.Clear();
        for (int i = 0; i < _frames.Count; i++)
        {
            var f = _frames[i];
            using var t = RenderFrame(f, 120, 28, 1.0, f.Width / 2.0, f.Height / 2.0, ReadAdjust());
            _items.Add(new Controls.TimelineItem
            {
                Index = i,
                Tick = i % 5 == 0 ? i.ToString() : "",
                Thumb = t == null ? BlankThumb() : ToBitmapImage(t),
            });
        }
        Timeline.SelectedIndex = _frames.Count == 0 ? -1 : Math.Clamp(_frameIndex, 0, _frames.Count - 1);
        UpdateFrameInfo();
    }

    private static BitmapImage BlankThumb()
    {
        using var b = new SD.Bitmap(120, 28, SDI.PixelFormat.Format24bppRgb);
        return ToBitmapImage(b);
    }

    // ---------- Timeline playback ----------

    private void TogglePlay()
    {
        if (_frames.Count < 2)
        {
            StatusText.Text = "Add at least 2 frames to play the animation (use “+ Frame”).";
            return;
        }
        _playing = !_playing;
        Timeline.SetPlaying(_playing);
        if (_playing)
        {
            // Match the timeline frame rate.
            _playTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, Fps()));
            _playTimer.Start();
        }
        else _playTimer.Stop();
    }

    private void StopPlay()
    {
        _playing = false;
        Timeline.SetPlaying(false);
        _playTimer.Stop();
        _frameIndex = 0;
        Timeline.SelectedIndex = _frames.Count > 0 ? 0 : -1;
        RenderView();
    }

    private void PlayTick()
    {
        if (_frames.Count == 0) return;
        _frameIndex = (_frameIndex + 1) % _frames.Count;
        Timeline.SelectedIndex = _frameIndex;
        RenderView();
    }

    private void StepBy(int delta)
    {
        if (_frames.Count == 0) return;
        int idx = delta == int.MinValue ? 0
            : delta == int.MaxValue ? _frames.Count - 1
            : _frameIndex + delta;
        _frameIndex = Math.Clamp(idx, 0, _frames.Count - 1);
        Timeline.SelectedIndex = _frameIndex;
        RenderView();
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
            ClearFrames();
            using var img = SD.Image.FromFile(path);
            _frames.Add(To32bpp(img));
            _frameIndex = 0;
            ResetView();
            StatusText.Text = System.IO.Path.GetFileName(path);
            RebuildThumbs();
            UpdateFrameInfo();
            RenderView();
            return true;
        }
        catch (Exception ex) { StatusText.Text = "Could not open: " + ex.Message; return false; }
    }

    private void ClearFrames()
    {
        foreach (var f in _frames) f.Dispose();
        _frames.Clear();
        _frameIndex = 0;
        _undo.Clear();
    }

    private double BaseScale(SD.Bitmap src) => Math.Max(VpW / src.Width, VpH / src.Height);
    private double BaseScale() => Current == null ? 1 : BaseScale(Current);

    private void ComputeCrop(SD.Bitmap src, double zoom, double cx, double cy,
        out double x, out double y, out double cw, out double ch)
    {
        double b = BaseScale(src);
        cw = Math.Min(VpW / (b * zoom), src.Width);
        ch = Math.Min(VpH / (b * zoom), src.Height);
        double qx = Math.Clamp(cx, cw / 2, src.Width - cw / 2);
        double qy = Math.Clamp(cy, ch / 2, src.Height - ch / 2);
        if (src.Width <= cw) qx = src.Width / 2;
        if (src.Height <= ch) qy = src.Height / 2;
        x = Math.Clamp(qx - cw / 2, 0, Math.Max(0, src.Width - cw));
        y = Math.Clamp(qy - ch / 2, 0, Math.Max(0, src.Height - ch));
    }

    private void ClampCenter(ref double cx, ref double cy, double zoom)
    {
        var cur = Current;
        if (cur == null) return;
        ComputeCrop(cur, zoom, cx, cy, out _, out _, out double cw, out double ch);
        cx = Math.Clamp(cx, cw / 2, cur.Width - cw / 2);
        cy = Math.Clamp(cy, ch / 2, cur.Height - ch / 2);
        if (cur.Width <= cw) cx = cur.Width / 2;
        if (cur.Height <= ch) cy = cur.Height / 2;
    }

    // ---------- Crop pan/zoom ----------

    private void Crop_Down(object sender, MouseButtonEventArgs e)
    {
        if (Current == null) return;
        CropImage.CaptureMouse();
        _dragging = true;
        _dragLast = e.GetPosition(CropImage);
        if (DrawCheck.IsChecked == true) PushUndo();
    }

    private void Crop_Move(object sender, MouseEventArgs e)
    {
        if (!_dragging || !CropImage.IsMouseCaptured || Current == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CropImage.ReleaseMouseCapture(); _dragging = false; return; }
        var p = e.GetPosition(CropImage);
        if (DrawCheck.IsChecked == true)
        {
            // Stamp along the segment; the timeline thumbnail is refreshed on
            // mouse-up only (rebuilding it per move made drawing feel laggy).
            DrawSegment(_dragLast, p);
            _dragLast = p;
            RenderView();
        }
        else
        {
            double s = BaseScale(Current) * _zoom;
            // Viewport bitmap is VpW wide but displayed at CropImage width (same here).
            double k = VpW / CropImage.ActualWidth;
            _cx -= (p.X - _dragLast.X) * k / s;
            _cy -= (p.Y - _dragLast.Y) * k / s;
            _dragLast = p;
            RenderView();
        }
    }

    private void Crop_Up(object sender, MouseButtonEventArgs e)
    {
        CropImage.ReleaseMouseCapture();
        _dragging = false;
        if (DrawCheck.IsChecked == true && Current != null)
        {
            RenderView();
            RebuildThumb(_frameIndex);
        }
    }

    private Point ViewToSource(Point view)
    {
        var cur = Current!;
        double k = VpW / CropImage.ActualWidth;
        ComputeCrop(cur, _zoom, _cx, _cy, out double x, out double y, out double cw, out double ch);
        return new Point(x + view.X * k / VpW * cw, y + view.Y * k / VpH * ch);
    }

    private void DrawSegment(Point viewFrom, Point viewTo)
    {
        var cur = Current;
        if (cur == null) return;
        var a = ViewToSource(viewFrom);
        var b = ViewToSource(viewTo);
        ComputeCrop(cur, _zoom, _cx, _cy, out _, out _, out double cw, out _);
        float w = (float)(PenSizeSlider.Value * cw / VpW);
        var c = _penColor;
        using var g = SD.Graphics.FromImage(cur);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        if (_brushTip != null)
        {
            StampStroke(g, a, b, w);
            return;
        }

        using var pen = new SD.Pen(SD.Color.FromArgb(c.A, c.R, c.G, c.B), Math.Max(1, w))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        // Dot for a click without movement.
        if (Math.Abs(b.X - a.X) < 0.5 && Math.Abs(b.Y - a.Y) < 0.5)
            g.FillEllipse(pen.Brush, (float)(a.X - w / 2), (float)(a.Y - w / 2), w, w);
        else
            g.DrawLine(pen, (float)a.X, (float)a.Y, (float)b.X, (float)b.Y);
    }

    // ---------- Brush stamps ----------

    /// <summary>Stamps the tinted brush tip along the stroke at even spacing.</summary>
    private void StampStroke(SD.Graphics g, Point a, Point b, float size)
    {
        var tip = _tintedTip;
        if (tip == null) return;
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double step = Math.Max(1.0, size * _brushSpacing);
        int count = Math.Max(1, (int)Math.Ceiling(dist / step));
        for (int i = 0; i <= count; i++)
        {
            double t = count == 0 ? 0 : (double)i / count;
            float x = (float)(a.X + dx * t);
            float y = (float)(a.Y + dy * t);
            var dst = new SD.RectangleF(x - size / 2, y - size / 2, size, size);
            g.DrawImage(tip, dst);
        }
    }

    /// <summary>Rebuild the tinted tip whenever brush/color/opacity changes.</summary>
    private void RebuildTintedTip()
    {
        _tintedTip?.Dispose();
        _tintedTip = null;
        if (_brushTip == null) return;
        var c = _penColor;
        var bmp = new SD.Bitmap(_brushTip.Width, _brushTip.Height, SDI.PixelFormat.Format32bppArgb);
        for (int y = 0; y < _brushTip.Height; y++)
            for (int x = 0; x < _brushTip.Width; x++)
            {
                var p = _brushTip.GetPixel(x, y);
                // Use the tip's coverage (max channel) as alpha, recolor to pen color.
                int cover = Math.Max(p.R, Math.Max(p.G, p.B));
                byte a = (byte)(cover * p.A / 255 * c.A / 255);
                bmp.SetPixel(x, y, SD.Color.FromArgb(a, c.R, c.G, c.B));
            }
        _tintedTip = bmp;
    }

    private string BrushesDir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VolumeOSD", "brushes");

    private void LoadBrushes()
    {
        try
        {
            System.IO.Directory.CreateDirectory(BrushesDir);
            BrushCombo.Items.Clear();
            BrushCombo.Items.Add(new ComboBoxItem { Content = "Round pen" });
            foreach (var f in System.IO.Directory.GetFiles(BrushesDir, "*.png"))
                BrushCombo.Items.Add(new ComboBoxItem { Content = System.IO.Path.GetFileNameWithoutExtension(f), Tag = f });
            BrushCombo.SelectedIndex = 0;
        }
        catch { }
    }

    private void Brush_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var item = BrushCombo.SelectedItem as ComboBoxItem;
        string? path = item?.Tag as string;
        _brushTip?.Dispose();
        _brushTip = null;
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            try { _brushTip = new SD.Bitmap(path); } catch { _brushTip = null; }
        }
        RebuildTintedTip();
        // Preview: show the tip (tinted) or a plain round dot.
        if (_brushTip != null)
        {
            BrushPreviewImg.Source = ToBitmapImage(_brushTip);
        }
        else
        {
            using var dot = new SD.Bitmap(48, 48, SDI.PixelFormat.Format32bppArgb);
            using (var g = SD.Graphics.FromImage(dot))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var br = new SD.SolidBrush(SD.Color.FromArgb(_penColor.A, _penColor.R, _penColor.G, _penColor.B));
                g.FillEllipse(br, 6, 6, 36, 36);
            }
            BrushPreviewImg.Source = ToBitmapImage(dot);
        }
    }

    private void Spacing_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpacingVal == null) return;
        _brushSpacing = Math.Clamp(e.NewValue / 100.0, 0.05, 2.0);
        SpacingVal.Text = $"{e.NewValue:F0}%";
    }

    private void ReloadBrushes_Click(object sender, RoutedEventArgs e)
    {
        LoadBrushes();
        StatusText.Text = $"Brushes reloaded from {BrushesDir}";
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
        if (AnimCheck.IsChecked == true && Current != null && KbCombo.SelectedIndex > 0)
            _animTimer.Start();
        else
        {
            _animTimer.Stop();
            RenderView();
        }
    }

    private void UpdateFrameInfo()
    {
        if (_frames.Count == 0)
        {
            FrameInfo.Text = "No image loaded";
            Timeline.SetFrameLabel("0 / 0");
            Timeline.SetInfo("No frames");
            return;
        }
        string f = _frames.Count == 1 ? "1 frame" : $"{_frames.Count} frames";
        if (KbCombo.SelectedIndex <= 0)
            FrameInfo.Text = _frames.Count > 1
                ? $"{f} · GIF exports the timeline @ {Fps()}fps"
                : "Still image — PNG snapshot available";
        else
        {
            int n = Math.Max(2, (int)Math.Round(Fps() * Duration()));
            FrameInfo.Text = $"{f} · Ken Burns {n} frames · {Duration():F0}s @ {Fps()}fps";
        }
        Timeline.SetFrameLabel($"{_frameIndex + 1} / {_frames.Count}");
        Timeline.SetInfo($"{f} · {Fps()} fps · click a frame to edit");
    }

    // ---------- Frames ----------

    private void Timeline_FrameSelected(int index)
    {
        if (!_ready || index < 0 || index >= _frames.Count) return;
        _frameIndex = index;
        Timeline.SetFrameLabel($"{_frameIndex + 1} / {_frames.Count}");
        RenderView();
    }

    private void RebuildThumb(int index)
    {
        if (index < 0 || index >= _frames.Count || index >= _items.Count) return;
        using var t = RenderFrame(_frames[index], 120, 28, 1.0,
            _frames[index].Width / 2.0, _frames[index].Height / 2.0, ReadAdjust());
        var old = _items[index];
        _items[index] = new Controls.TimelineItem
        {
            Index = index,
            Tick = old.Tick,
            Thumb = t == null ? BlankThumb() : ToBitmapImage(t),
        };
    }

    private void FrameAdd_Click(object sender, RoutedEventArgs e)
    {
        int w = Current?.Width ?? 872, h = Current?.Height ?? 192;
        var blank = new SD.Bitmap(w, h, SDI.PixelFormat.Format32bppArgb);
        using (var g = SD.Graphics.FromImage(blank))
            g.Clear(SD.Color.Transparent);
        _frames.Add(blank);
        _frameIndex = _frames.Count - 1;
        ResetView();
        RebuildThumbs();
        UpdateFrameInfo();
        RenderView();
    }

    private void FrameDupe_Click(object sender, RoutedEventArgs e)
    {
        var cur = Current;
        if (cur == null) return;
        _frames.Insert(_frameIndex + 1, (SD.Bitmap)cur.Clone());
        _frameIndex++;
        RebuildThumbs();
        UpdateFrameInfo();
        RenderView();
    }

    private void FrameDel_Click(object sender, RoutedEventArgs e)
    {
        if (_frames.Count <= 1) return;
        _frames[_frameIndex].Dispose();
        _frames.RemoveAt(_frameIndex);
        _frameIndex = Math.Clamp(_frameIndex, 0, _frames.Count - 1);
        ResetView();
        RebuildThumbs();
        UpdateFrameInfo();
        RenderView();
    }

    private void ClearFrame_Click(object sender, RoutedEventArgs e)
    {
        var cur = Current;
        if (cur == null) return;
        PushUndo();
        using var g = SD.Graphics.FromImage(cur);
        g.Clear(SD.Color.Transparent);
        RenderView();
        RebuildThumb(_frameIndex);
    }

    // ---------- Undo ----------

    private void PushUndo()
    {
        var cur = Current;
        if (cur == null) return;
        _undo.Add((_frameIndex, (SD.Bitmap)cur.Clone()));
        if (_undo.Count > 20)
        {
            _undo[0].Snap.Dispose();
            _undo.RemoveAt(0);
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0 || Current == null) return;
        var (idx, snap) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        int at = (idx >= 0 && idx < _frames.Count) ? idx : _frameIndex;
        _frames[at].Dispose();
        _frames[at] = snap;
        _frameIndex = at;
        RebuildThumbs();
        RenderView();
    }

    // ---------- Pen ----------

    private void PenSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PenSizeVal != null) PenSizeVal.Text = $"{e.NewValue:F0}";
    }

    private void PenColor_Click(object sender, RoutedEventArgs e)
    {
        var c = _penColor;
        var dlg = new ColorPickerDialog(Color.FromRgb(c.R, c.G, c.B)) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _penColor = Color.FromRgb(dlg.SelectedColor.R, dlg.SelectedColor.G, dlg.SelectedColor.B);
            PenColorBtn.Background = new SolidColorBrush(_penColor);
            RebuildTintedTip();
            // Refresh the preview dot color too.
            if (_brushTip == null) Brush_Changed(this, new SelectionChangedEventArgs(
                System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new List<object>(), new List<object>()));
        }
    }

    // ---------- GIF import ----------

    private void ImportGif_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "GIF animation|*.gif|All files|*.*",
            Title = "Add a GIF's frames to the timeline",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            using var img = SD.Image.FromFile(dlg.FileName);
            var dim = new SDI.FrameDimension(img.FrameDimensionsList[0]);
            int n = img.GetFrameCount(dim);
            int added = 0;
            for (int i = 0; i < n; i++)
            {
                img.SelectActiveFrame(dim, i);
                var frame = NormalizeFrame(new SD.Bitmap(img));
                _frames.Add(frame);
                added++;
            }
            _frameIndex = _frames.Count - added;
            ResetView();
            RebuildThumbs();
            UpdateFrameInfo();
            RenderView();
            StatusText.Text = $"Imported {added} frame(s) from {System.IO.Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex) { StatusText.Text = "GIF import failed: " + ex.Message; }
    }

    /// <summary>Fit a frame into the current canvas (or adopt the first frame's size).</summary>
    private SD.Bitmap NormalizeFrame(SD.Bitmap src)
    {
        int w = _frames.Count == 0 ? src.Width : _frames[0].Width;
        int h = _frames.Count == 0 ? src.Height : _frames[0].Height;
        var bmp = new SD.Bitmap(w, h, SDI.PixelFormat.Format32bppArgb);
        using var g = SD.Graphics.FromImage(bmp);
        g.Clear(SD.Color.Transparent);
        double s = Math.Min((double)w / src.Width, (double)h / src.Height);
        int dw = (int)(src.Width * s), dh = (int)(src.Height * s);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, (w - dw) / 2, (h - dh) / 2, dw, dh);
        src.Dispose();
        return bmp;
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

    private SD.Bitmap? RenderFrame(SD.Bitmap src, int outW, int outH, double zoom, double cx, double cy, AdjustState adj)
    {
        double b = BaseScale(src);
        double cw = Math.Min(VpW / (b * zoom), src.Width);
        double ch = Math.Min(VpH / (b * zoom), src.Height);
        // (cx,cy) is the crop CENTER (pre-clamped) — top-left = center minus half size.
        double x = cx - cw / 2, y = cy - ch / 2;
        x = Math.Clamp(x, 0, Math.Max(0, src.Width - cw));
        y = Math.Clamp(y, 0, Math.Max(0, src.Height - ch));
        var bmp = new SD.Bitmap(outW, outH, SDI.PixelFormat.Format24bppRgb);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            using var attrs = new SDI.ImageAttributes();
            attrs.SetColorMatrix(new SDI.ColorMatrix(AdjustMatrix(adj)));
            g.DrawImage(src,
                new SD.Rectangle(0, 0, outW, outH),
                (float)x, (float)y, (float)cw, (float)ch,
                SD.GraphicsUnit.Pixel, attrs);
        }
        return bmp;
    }

    private void RenderView() => RenderView(_zoom, _cx, _cy);

    private void RenderView(double zoom, double cx, double cy)
    {
        var cur = Current;
        if (!_ready || cur == null) return;
        ClampCenter(ref cx, ref cy, zoom);
        if (!_animTimer.IsEnabled) { _cx = cx; _cy = cy; }
        using var bmp = RenderFrame(cur, 480, 106, zoom, cx, cy, ReadAdjust());
        if (bmp == null) return;
        // Onion skin: ghost the previous frame underneath (view only, never exported).
        if (OnionCheck.IsChecked == true && _frameIndex > 0 && _frameIndex < _frames.Count)
        {
            using var prev = RenderFrame(_frames[_frameIndex - 1], 480, 106, zoom, cx, cy, ReadAdjust());
            if (prev != null)
            {
                using var g = SD.Graphics.FromImage(bmp);
                using var attrs = new SDI.ImageAttributes();
                var m = new float[][] {
                    new float[] { 1, 0, 0, 0, 0 }, new float[] { 0, 1, 0, 0, 0 },
                    new float[] { 0, 0, 1, 0, 0 }, new float[] { 0, 0, 0, 0.35f, 0 },
                    new float[] { 0, 0, 0, 0, 1 } };
                attrs.SetColorMatrix(new SDI.ColorMatrix(m));
                g.DrawImage(prev, new SD.Rectangle(0, 0, 480, 106),
                    0, 0, 480, 106, SD.GraphicsUnit.Pixel, attrs);
            }
        }
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
        var cur = Current;
        if (cur == null) return;
        _animT += 0.033 / Duration();
        if (_animT > 1) _animT -= 1;
        double p = _animT, z = _zoom, cx = _cx, cy = _cy;
        double b = BaseScale(cur);
        switch (KbCombo.SelectedIndex)
        {
            case 1: z = _zoom * (1 + 0.3 * p); break;                    // zoom in
            case 2: z = _zoom * (1.3 - 0.3 * p); break;                  // zoom out
            case 3: // pan sideways (ensure slack, sweep center)
                z = Math.Max(_zoom, 1.15);
                double cw = Math.Min(VpW / (b * z), cur.Width);
                double lo = cw / 2, hi = cur.Width - cw / 2;
                cx = lo + (hi - lo) * p;
                break;
        }
        RenderView(z, cx, cy);
    }

    // ---------- Export ----------

    private void Png_Click(object sender, RoutedEventArgs e)
    {
        var cur = Current;
        if (cur == null || _exporting) return;
        try
        {
            using var bmp = RenderFrame(cur, 872, 192, _zoom, _cx, _cy, ReadAdjust());
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
        var cur = Current;
        if (cur == null || _exporting) return;
        int kbMode = KbCombo.SelectedIndex;
        bool doKenBurns = kbMode > 0;
        if (!doKenBurns && _frames.Count < 2)
        {
            StatusText.Text = "Add frames or pick a Ken Burns mode first.";
            return;
        }
        _exporting = true;
        try
        {
            int fps = Fps();
            int n = doKenBurns ? Math.Max(2, (int)Math.Round(fps * Duration())) : _frames.Count;
            double z0 = _zoom, cx0 = _cx, cy0 = _cy;
            var adj = ReadAdjust();
            // Snapshot frames (thread-safe copies) for the background render.
            var srcs = new List<SD.Bitmap>(n);
            try
            {
                if (doKenBurns)
                {
                    for (int i = 0; i < n; i++) srcs.Add((SD.Bitmap)cur.Clone());
                }
                else
                {
                    foreach (var f in _frames) srcs.Add((SD.Bitmap)f.Clone());
                }
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "volwall.gif");
                await Task.Run(() =>
                {
                    var frames = new List<SD.Bitmap>(n);
                    try
                    {
                        for (int i = 0; i < n; i++)
                        {
                            double p = n == 1 ? 0 : (double)i / (n - 1);
                            SD.Bitmap src = doKenBurns ? srcs[0] : srcs[i];
                            double z = z0, cx = cx0, cy = cy0;
                            if (doKenBurns)
                            {
                                double b = BaseScale(src);
                                switch (kbMode)
                                {
                                    case 1: z = z0 * (1 + 0.3 * p); break;
                                    case 2: z = z0 * (1.3 - 0.3 * p); break;
                                    case 3:
                                        z = Math.Max(z0, 1.15);
                                        double cw = Math.Min(VpW / (b * z), src.Width);
                                        double lo = cw / 2, hi = src.Width - cw / 2;
                                        cx = lo + (hi - lo) * p;
                                        break;
                                }
                            }
                            var f = RenderFrame(src, 436, 96, z, cx, cy, adj);
                            if (f != null) frames.Add(f);
                            int done = i + 1;
                            if (done % 6 == 0 || done == n)
                                Dispatcher.Invoke(() => StatusText.Text = $"Rendering {done}/{n}…");
                        }
                        frames.SaveAsAnimatedGif(tmp, TimeSpan.FromMilliseconds(1000.0 / fps), null, null);
                        Dispatcher.Invoke(() =>
                        {
                            _main.ImportWallpaperFile(tmp, ".gif");
                            StatusText.Text = $"GIF applied ({frames.Count} frames).";
                        });
                    }
                    finally
                    {
                        foreach (var f in frames) f.Dispose();
                        try { System.IO.File.Delete(tmp); } catch { }
                    }
                });
            }
            finally
            {
                foreach (var s in srcs) s.Dispose();
            }
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
        ClearFrames();
        foreach (var (_, snap) in _undo) snap.Dispose();
        _undo.Clear();
        base.OnClosed(e);
    }
}
