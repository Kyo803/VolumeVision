using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VolumeOSD;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _main;
    private bool _loading = true;
    private readonly Dictionary<string, Border> _swatches = new();

    private static readonly (string Key, string Label)[] ColorRows =
    {
        ("bg", "Background"), ("border", "Border Ring"), ("trackFill", "Track fill"),
        ("tint", "Audio Output Tint"), ("ring", "Progress Ring"), ("icons", "Icons"),
    };

    public SettingsWindow(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        InitSwatches();
        RefreshAll();
        RefreshWallpaperPreview();
        _loading = false;
    }

    private void InitSwatches()
    {
        _swatches["bg"] = SwatchBg;
        _swatches["border"] = SwatchBorder;
        _swatches["trackFill"] = SwatchTrackFill;
        _swatches["tint"] = SwatchTint;
        _swatches["ring"] = SwatchRing;
        _swatches["icons"] = SwatchIcons;
    }

    // ---------- Refresh ----------

    private void RefreshAll()
    {
        foreach (var (key, _) in ColorRows)
            PaintSwatch(key);
        GlassSlider.Value = _main.Glass * 100;
        GlossSlider.Value = _main.Gloss * 100;
        SizeSlider.Value = _main.UiScale * 100;
        BgOnlyCheck.IsChecked = _main.BgOnly;
        BgName.Text = _main.BgImageName;
        foreach (var b in new[] { PosTop, PosBottom, PosLeft, PosRight })
            b.Opacity = ((b.Tag as string) == _main.Position) ? 1.0 : 0.52;
        RefreshPreviewPills();
        RefreshWallpaperPreview();
        HotkeyHint.Text = $"Hotkeys: {_main.SettingsHotkeyLabel} settings · Ctrl+Shift+V summon · Ctrl+Shift+Plus/Minus resize · Alt+X/S overlay · Alt+Z/C prev/next";
    }

    private void PaintSwatch(string key)
    {
        if (!_swatches.TryGetValue(key, out var swatch)) return;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(_main.GetColorHex(key));
            swatch.Background = new SolidColorBrush(c);
        }
        catch { swatch.Background = new SolidColorBrush(Colors.Gray); }
    }

    private void RefreshPreviewPills()
    {
        // System track = current system volume scaled to preview width
        double vol = _main.SystemVolume;
        SysTrackPreview.Width = Math.Max(4, 80 * vol);
    }

    private void RefreshWallpaperPreview()
    {
        try
        {
            string file = _main.WallpaperFilePath;
            bool has = !string.IsNullOrEmpty(file) && System.IO.File.Exists(file);
            // Show animation on all three previews, respect bgOnly gate
            bool show = has && _main.BgOnly;
            foreach (var img in new[] { SysPreviewBg, SpotPreviewBg, LivePreviewBg })
            {
                if (img == null) continue;
                if (show)
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(file);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        if (file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                        {
                            WpfAnimatedGif.ImageBehavior.SetAnimatedSource(img, bmp);
                            WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(img, System.Windows.Media.Animation.RepeatBehavior.Forever);
                        }
                        else
                        {
                            WpfAnimatedGif.ImageBehavior.SetAnimatedSource(img, null);
                            img.Source = bmp;
                        }
                        img.Visibility = Visibility.Visible;
                    }
                    catch { img.Visibility = Visibility.Collapsed; }
                }
                else
                {
                    WpfAnimatedGif.ImageBehavior.SetAnimatedSource(img, null);
                    img.Source = null;
                    img.Visibility = Visibility.Collapsed;
                }
            }
            foreach (var tint in new[] { SysPreviewTint, SpotPreviewTint, LivePreviewTint })
                if (tint != null) tint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { }
    }

    // ---------- Events ----------

    private void Swatch_Click(object sender, MouseButtonEventArgs e)
    {
        string key = (sender as Border)?.Tag as string ?? "";
        Color start = Colors.White;
        try { start = (Color)ColorConverter.ConvertFromString(_main.GetColorHex(key)); } catch { }
        var dlg = new ColorPickerDialog(start) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var picked = dlg.SelectedColor;
            string hex = picked.A == 255
                ? $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}"
                : $"#{picked.A:X2}{picked.R:X2}{picked.G:X2}{picked.B:X2}";
            _main.SetColorHex(key, hex);
            PaintSwatch(key);
        }
    }

    private void Glass_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GlassVal == null) return;
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
    }

    private void Size_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SizeVal == null) return;
        SizeVal.Text = $"{e.NewValue:F0}%";
        if (_loading) return;
        _main.ApplyScale(e.NewValue / 100.0);
    }

    private void BgPick_Click(object sender, RoutedEventArgs e)
    {
        _main.PickBackgroundImage();
        BgName.Text = _main.BgImageName;
        BgOnlyCheck.IsChecked = _main.BgOnly;
        RefreshWallpaperPreview();
    }

    private void BgApply_Click(object sender, RoutedEventArgs e)
    {
        _main.ReapplyBackground();
        BgName.Text = _main.BgImageName;
        RefreshWallpaperPreview();
    }

    private void BgClear_Click(object sender, RoutedEventArgs e)
    {
        _main.ClearBackgroundImage();
        BgName.Text = _main.BgImageName;
        RefreshWallpaperPreview();
    }

    private void BgOnly_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _main.SetBgOnly(BgOnlyCheck.IsChecked == true);
        RefreshWallpaperPreview();
    }

    private void Pos_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        string pos = (sender as Button)?.Tag as string ?? "Bottom";
        _main.ApplyPosition(pos);
        foreach (var b in new[] { PosTop, PosBottom, PosLeft, PosRight })
            b.Opacity = ((b.Tag as string) == _main.Position) ? 1.0 : 0.52;
    }

    private void Studio_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var studio = new WallpaperStudio(_main) { Owner = this };
            studio.ShowDialog();
            BgName.Text = _main.BgImageName;
            RefreshWallpaperPreview();
        }
        catch (Exception ex) { BgName.Text = "Studio failed: " + ex.Message; }
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
