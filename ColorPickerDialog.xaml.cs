using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VolumeOSD;

public partial class ColorPickerDialog : Window
{
    public Color SelectedColor { get; private set; }

    private double _h; // 0..360
    private double _s; // 0..1
    private double _v; // 0..1
    private bool _updating;

    public ColorPickerDialog(Color initial)
    {
        InitializeComponent();
        SelectedColor = initial;
        CurrentSwatch.Background = new SolidColorBrush(initial);
        (_h, _s, _v) = ToHsv(initial);
        RefreshUI();
    }

    // ---------- HSV ----------

    public static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static Color FromHsv(double h, double s, double v, byte a = 255)
    {
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromArgb(a,
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private Color Current => FromHsv(_h, _s, _v, SelectedColor.A);

    private void RefreshUI()
    {
        _updating = true;
        try
        {
            var hue = FromHsv(_h, 1, 1);
            SvBase.Background = new SolidColorBrush(hue);
            Canvas.SetLeft(SvThumb, _s * SvCanvas.Width - SvThumb.Width / 2);
            Canvas.SetTop(SvThumb, (1 - _v) * SvCanvas.Height - SvThumb.Height / 2);
            Canvas.SetTop(HueMarker, _h / 360 * HueCanvas.Height - HueMarker.Height / 2);
            var rgb = Current;
            NewSwatch.Background = new SolidColorBrush(rgb);
            RBox.Text = rgb.R.ToString();
            GBox.Text = rgb.G.ToString();
            BBox.Text = rgb.B.ToString();
            HexBox.Text = $"{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
            HVal.Text = $"{_h:F0}°";
            SVal.Text = $"{_s * 100:F0}%";
            BVal.Text = $"{_v * 100:F0}%";
        }
        finally { _updating = false; }
    }

    // ---------- SV square ----------

    private void Sv_Down(object sender, MouseButtonEventArgs e)
    {
        SvCanvas.CaptureMouse();
        SetSv(e.GetPosition(SvCanvas));
    }

    private void Sv_Move(object sender, MouseEventArgs e)
    {
        if (!SvCanvas.IsMouseCaptured) return;
        if (e.LeftButton != MouseButtonState.Pressed) { SvCanvas.ReleaseMouseCapture(); return; }
        SetSv(e.GetPosition(SvCanvas));
    }

    private void Sv_Up(object sender, MouseButtonEventArgs e) => SvCanvas.ReleaseMouseCapture();

    private void SetSv(Point p)
    {
        _s = Math.Clamp(p.X / SvCanvas.ActualWidth, 0, 1);
        _v = Math.Clamp(1 - p.Y / SvCanvas.ActualHeight, 0, 1);
        RefreshUI();
    }

    // ---------- Hue strip ----------

    private void Hue_Down(object sender, MouseButtonEventArgs e)
    {
        HueCanvas.CaptureMouse();
        SetHue(e.GetPosition(HueCanvas));
    }

    private void Hue_Move(object sender, MouseEventArgs e)
    {
        if (!HueCanvas.IsMouseCaptured) return;
        if (e.LeftButton != MouseButtonState.Pressed) { HueCanvas.ReleaseMouseCapture(); return; }
        SetHue(e.GetPosition(HueCanvas));
    }

    private void Hue_Up(object sender, MouseButtonEventArgs e) => HueCanvas.ReleaseMouseCapture();

    private void SetHue(Point p)
    {
        _h = Math.Clamp(p.Y / HueCanvas.ActualHeight, 0, 1) * 360;
        RefreshUI();
    }

    // ---------- Fields ----------

    private void Rgb_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        if (byte.TryParse(RBox.Text, out byte r) && byte.TryParse(GBox.Text, out byte g) && byte.TryParse(BBox.Text, out byte b))
        {
            (_h, _s, _v) = ToHsv(Color.FromRgb(r, g, b));
            RefreshUI();
        }
    }

    private void Hex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        string t = HexBox.Text.Trim().TrimStart('#');
        if (t.Length == 6 && uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out uint v))
        {
            var c = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            (_h, _s, _v) = ToHsv(c);
            RefreshUI();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedColor = Current;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
