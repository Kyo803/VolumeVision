using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VolumeOSD;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _main;
    private bool _loading = true;

    private static readonly (string Key, string Label)[] ColorRows =
    {
        ("bg", "Background"), ("border", "Border ring"), ("track", "Track"),
        ("trackFill", "Track fill"), ("tint", "Spotify tint"),
        ("ring", "Progress ring"), ("icons", "Icons"),
    };

    private readonly Dictionary<string, TextBox> _boxes = new();
    private readonly Dictionary<string, Button> _swatches = new();

    public SettingsWindow(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        BuildColorRows();
        RefreshAll();
        _loading = false;
    }

    private void BuildColorRows()
    {
        for (int i = 0; i < ColorRows.Length; i++)
        {
            var (key, label) = ColorRows[i];
            var lb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
            var tb = new TextBox
            {
                Margin = new Thickness(0, 2, 0, 2),
                Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x22)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3A)),
                Tag = key,
            };
            tb.TextChanged += ColorBox_Changed;
            var sw = new Button
            {
                Margin = new Thickness(6, 2, 0, 2),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = key,
                ToolTip = "Pick visually…",
            };
            sw.Click += Swatch_Click;
            Grid.SetRow(lb, i);
            Grid.SetRow(tb, i);
            Grid.SetRow(sw, i);
            Grid.SetColumn(tb, 1);
            Grid.SetColumn(sw, 2);
            ColorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ColorGrid.Children.Add(lb);
            ColorGrid.Children.Add(tb);
            ColorGrid.Children.Add(sw);
            _boxes[key] = tb;
            _swatches[key] = sw;
        }
    }

    private void RefreshAll()
    {
        foreach (var (key, _) in ColorRows)
        {
            _boxes[key].Text = _main.GetColorHex(key);
            PaintSwatch(key);
        }
        GlassSlider.Value = _main.Glass * 100;
        GlossSlider.Value = _main.Gloss * 100;
        ScaleSlider.Value = _main.UiScale * 100;
        GlassVal.Text = $"{GlassSlider.Value:F0}%";
        GlossVal.Text = $"{GlossSlider.Value:F0}%";
        ScaleVal.Text = $"{ScaleSlider.Value:F0}%";
        PreviewGloss.Opacity = 0.55 * _main.Gloss;
        HotkeyHint.Text = $"Hotkeys: {_main.SettingsHotkeyLabel} settings · Ctrl+Shift+V summon · Ctrl+Shift+Plus/Minus resize. Alt+X/S overlay · {_main.PrevHotkeyLabel}/Alt+C prev/next · Alt+Shift+Z/C frames · Alt+Shift+A/D volume. Double-click empty pill area for settings.";
        MarkPosition();
        BgName.Text = _main.BgImageName;
        FxCheck.IsChecked = _main.SliderFx;
    }

    private void MarkPosition()
    {
        var on = new SolidColorBrush(Color.FromRgb(0x2D, 0xA6, 0x3B));
        var off = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));
        foreach (var b in new[] { PosTop, PosBottom, PosLeft, PosRight })
        {
            bool sel = (b.Tag as string) == _main.Position;
            b.Background = sel ? on : off;
            b.Foreground = Brushes.White;
        }
        PosHint.Text = _main.Position switch
        {
            "Top" => "Top-center dock",
            "Left" => "Left edge, pill turned 90°",
            "Right" => "Right edge, pill turned 90°",
            _ => "Bottom-center dock",
        };
    }

    private void BgPick_Click(object sender, RoutedEventArgs e)
    {
        _main.PickBackgroundImage();
        BgName.Text = _main.BgImageName;
    }

    private void BgClear_Click(object sender, RoutedEventArgs e)
    {
        _main.ClearBackgroundImage();
        BgName.Text = _main.BgImageName;
    }

    private void Studio_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var studio = new WallpaperStudio(_main) { Owner = this };
            studio.ShowDialog();
            BgName.Text = _main.BgImageName;
        }
        catch (Exception ex) { BgName.Text = "Studio failed: " + ex.Message; }
    }

    private void Fx_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _main.SetSliderFx(FxCheck.IsChecked == true);
    }

    private void Pos_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _main.ApplyPosition((sender as Button)?.Tag as string ?? "Bottom");
        MarkPosition();
    }

    private void PaintSwatch(string key)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(_boxes[key].Text.Trim());
            _swatches[key].Background = new SolidColorBrush(c);
        }
        catch { }
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        string key = ((Button)sender).Tag as string ?? "";
        Color start = Colors.White;
        try { start = (Color)ColorConverter.ConvertFromString(_main.GetColorHex(key)); } catch { }
        var dlg = new ColorPickerDialog(Color.FromRgb(start.R, start.G, start.B)) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            // Preserve the original alpha (e.g. the 20% Spotify tint).
            var picked = dlg.SelectedColor;
            byte a = start.A;
            string hex = a == 255
                ? $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}"
                : $"#{a:X2}{picked.R:X2}{picked.G:X2}{picked.B:X2}";
            _boxes[key].Text = hex; // TextChanged applies + repaints swatch
        }
    }

    private void ColorBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        var tb = (TextBox)sender;
        string key = (tb.Tag as string) ?? "";
        bool ok = _main.SetColorHex(key, tb.Text.Trim());
        tb.BorderBrush = new SolidColorBrush(ok ? Color.FromRgb(0x33, 0x33, 0x3A) : Colors.Red);
        if (ok) PaintSwatch(key);
    }

    private void Glass_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GlassVal == null) return; // fires during InitializeComponent, before fields exist
        GlassVal.Text = $"{e.NewValue:F0}%";
        if (_loading) return;
        _main.SetGlass(e.NewValue / 100.0);
    }

    private void Gloss_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GlossVal == null) return;
        GlossVal.Text = $"{e.NewValue:F0}%";
        if (_loading) return;
        _main.SetGloss(e.NewValue / 100.0);
        if (PreviewGloss != null) PreviewGloss.Opacity = 0.55 * _main.Gloss;
    }

    private void Scale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleVal == null) return;
        ScaleVal.Text = $"{e.NewValue:F0}%";
        if (_loading) return;
        _main.ApplyScale(e.NewValue / 100.0);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _main.ResetAppearance();
        _loading = true;
        RefreshAll();
        _loading = false;
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        VolumeOSD.Services.Uninstaller.RunUninstall(this);
    }
}
