using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VolumeOSD.Services;
using WpfAnimatedGif;

namespace VolumeOSD;

public partial class MainWindow : Window
{
    private enum OsdMode { System, Spotify }

    private readonly VolumeService _vol = new();
    private readonly VolumeKeyHook _hook = new();
    private readonly SpotifyService _spot = new();
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(3000) };
    private readonly DispatcherTimer _spotTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private OsdMode _mode = OsdMode.System;
    private OsdMode _shownMode = OsdMode.System; // frame currently (or transition-target) displayed
    private int _transitionToken; // guards stale collapse timers on rapid toggling
    private double _uiScale = 300.0 / 218.0; // compact default; Ctrl+Shift+Plus/Minus adjusts, persisted
    private const double FigmaW = 218, FigmaH = 48;
    private string _lastTitle = "";
    private bool _spotRunning;
    private bool _hasSession;
    private string _spotTitle = "";
    private string _spotArtist = "";
    private string _spotTip = "";
    private string _artKey = ""; // track whose thumbnail is currently shown/fetched

    private void UpdateSpotifyTip()
    {
        string tip;
        if (_hasSession && !string.IsNullOrEmpty(_spotTitle))
            tip = $"{_spotTitle} — {_spotArtist}";
        else if (_spotRunning)
            tip = "Spotify";
        else
            tip = "No media playing";
        if (tip == _spotTip) return;
        _spotTip = tip;
        SysPlayBtn.ToolTip = tip;
        SpotPlayBtn.ToolTip = tip;
        SysPrevBtn.ToolTip = tip;
        SysNextBtn.ToolTip = tip;
        SpotPrevBtn.ToolTip = tip;
        SpotNextBtn.ToolTip = tip;
    }

    public MainWindow()
    {
        DebugLog.Write("MainWindow ctor enter");
        InitializeComponent();
        DebugLog.Write("MainWindow ctor InitializeComponent ok");
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        DebugLog.Write("MainWindow ctor done");
    }

    // Ctrl+Shift+V summons the pill even if volume keys never reach the hook.
    // Ctrl+Shift+Plus/Minus live-resizes it (persisted to %AppData%\VolumeOSD).
    private const int HOTKEY_ID = 0x5ADD;
    private const int HOTKEY_BIGGER = 0x5ADE;
    private const int HOTKEY_SMALLER = 0x5ADF;
    private const int HOTKEY_SETTINGS = 0x5AE0;
    // Custom Alt combos: 2-key = summon/transport, 3-key = volume control.
    private const int HOTKEY_ALTX = 0x5AE1;
    private const int HOTKEY_ALTS = 0x5AE2;
    private const int HOTKEY_ALTZ = 0x5AE3;
    private const int HOTKEY_ALTC = 0x5AE4;
    private const int HOTKEY_ALTSHIFT_Z = 0x5AE5;
    private const int HOTKEY_ALTSHIFT_C = 0x5AE6;
    private const int HOTKEY_ALTSHIFT_A = 0x5AE7;
    private const int HOTKEY_ALTSHIFT_D = 0x5AE8;
    private const int WM_HOTKEY = 0x0312;

    public string SettingsHotkeyLabel { get; private set; } = "double-click the pill";
    public string PrevHotkeyLabel { get; private set; } = "Alt+Z";

    private SettingsWindow? _settings;
    private Services.VolumeTray? _tray;

    /// <summary>Bring the pill up in whatever frame is active.</summary>
    public void Summon() => ShowOsd(_mode);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        // NOTE: AcrylicHelper.EnableBlur intentionally NOT called — SetWindowCompositionAttribute
        // acrylic on an AllowsTransparency window kills per-pixel alpha and paints the
        // transparent window area black. The pill keeps its #BA000000 fill which WPF
        // composites over the desktop itself (translucency without live backdrop blur).
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            const uint MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_ALT = 0x0001;
            bool ok = RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, 0x56 /* V */);
            bool okBig = RegisterHotKey(hwnd, HOTKEY_BIGGER, MOD_CONTROL | MOD_SHIFT, 0xBB /* Plus */);
            bool okSmall = RegisterHotKey(hwnd, HOTKEY_SMALLER, MOD_CONTROL | MOD_SHIFT, 0xBD /* Minus */);
            bool okSet = RegisterHotKey(hwnd, HOTKEY_SETTINGS, MOD_CONTROL | MOD_SHIFT, 0x53 /* S */);
            if (okSet) SettingsHotkeyLabel = "Ctrl+Shift+S";
            else
            {
                // S is often claimed by other apps — fall back to O.
                okSet = RegisterHotKey(hwnd, HOTKEY_SETTINGS, MOD_CONTROL | MOD_SHIFT, 0x4F /* O */);
                if (okSet) SettingsHotkeyLabel = "Ctrl+Shift+O";
            }
            // Alt combos (global, work from any app).
            bool okX = RegisterHotKey(hwnd, HOTKEY_ALTX, MOD_ALT, 0x58 /* X */);
            bool okS = RegisterHotKey(hwnd, HOTKEY_ALTS, MOD_ALT, 0x53 /* S */);
            bool okZ = RegisterHotKey(hwnd, HOTKEY_ALTZ, MOD_ALT, 0x5A /* Z */);
            string prevLabel = "Alt+Z";
            if (!okZ)
            {
                // Z is often claimed (GPU overlays, macro tools) — fall back to Q.
                okZ = RegisterHotKey(hwnd, HOTKEY_ALTZ, MOD_ALT, 0x51 /* Q */);
                if (okZ) prevLabel = "Alt+Q";
            }
            PrevHotkeyLabel = okZ ? prevLabel : "unavailable";
            bool okC = RegisterHotKey(hwnd, HOTKEY_ALTC, MOD_ALT, 0x43 /* C */);
            bool okSZ = RegisterHotKey(hwnd, HOTKEY_ALTSHIFT_Z, MOD_ALT | MOD_SHIFT, 0x5A);
            bool okSC = RegisterHotKey(hwnd, HOTKEY_ALTSHIFT_C, MOD_ALT | MOD_SHIFT, 0x43);
            bool okSA = RegisterHotKey(hwnd, HOTKEY_ALTSHIFT_A, MOD_ALT | MOD_SHIFT, 0x41 /* A */);
            bool okSD = RegisterHotKey(hwnd, HOTKEY_ALTSHIFT_D, MOD_ALT | MOD_SHIFT, 0x44 /* D */);
            DebugLog.Write($"RegisterHotKey summon ok={ok} bigger ok={okBig} smaller ok={okSmall} settings ok={okSet} [{SettingsHotkeyLabel}] hwnd={hwnd}");
            DebugLog.Write($"Alt hotkeys X={okX} S={okS} Z={okZ} C={okC} ShiftZ={okSZ} ShiftC={okSC} ShiftA={okSA} ShiftD={okSD}");
            HwndSource.FromHwnd(hwnd)?.AddHook(WndHook);
        }
        catch (Exception ex) { DebugLog.Write("hotkey setup FAIL: " + ex.Message); }
    }

    private IntPtr WndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ID)
            {
                DebugLog.Write("HOTKEY pressed -> summon");
                ShowOsd(OsdMode.System);
                handled = true;
            }
            else if (id == HOTKEY_BIGGER)
            {
                ApplyScale(_uiScale + 0.15);
                ShowOsd(_mode);
                handled = true;
            }
            else if (id == HOTKEY_SMALLER)
            {
                ApplyScale(_uiScale - 0.15);
                ShowOsd(_mode);
                handled = true;
            }
            else if (id == HOTKEY_SETTINGS)
            {
                OpenSettings();
                handled = true;
            }
            else if (id == HOTKEY_ALTX || id == HOTKEY_ALTS)
            {
                DebugLog.Write("HOTKEY summon overlay");
                ShowOsd(_mode);
                handled = true;
            }
            else if (id == HOTKEY_ALTZ)
            {
                _ = PrevTrackAsync();
                handled = true;
            }
            else if (id == HOTKEY_ALTC)
            {
                _ = NextTrackAsync();
                handled = true;
            }
            else if (id == HOTKEY_ALTSHIFT_Z)
            {
                ShowOsd(OsdMode.System);
                handled = true;
            }
            else if (id == HOTKEY_ALTSHIFT_C)
            {
                ShowOsd(OsdMode.Spotify);
                _ = RefreshSpotifyAsync();
                handled = true;
            }
            else if (id == HOTKEY_ALTSHIFT_A)
            {
                AdjustActiveSlider(-0.05f);
                handled = true;
            }
            else if (id == HOTKEY_ALTSHIFT_D)
            {
                AdjustActiveSlider(0.05f);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    // ---------- Appearance model (colors, grading, position, scale) ----------

    private static string SettingsPath => System.IO.Path.Combine(AppDataDir, "settings.json");
    private static string AppDataDir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VolumeOSD");

    // Wallpaper (filename inside AppDataDir) + animated slider shimmer toggle.
    private string _bgImageFile = "";
    public string BgImageName => string.IsNullOrEmpty(_bgImageFile) ? "None (solid color)" : _bgImageFile;
    public bool SliderFx { get; private set; } = true;
    /// <summary>When true (and a wallpaper is set), the solid glass color is
    /// dropped so the animation shows through at full vibrancy.</summary>
    public bool BgOnly { get; private set; }
    private Storyboard? _shimmerStory;

    // Figma defaults
    private const string DefBg = "#000000";
    private const string DefBorder = "#E8E8E8";
    private const string DefTrack = "#3A3A3A";
    private const string DefTrackFill = "#8A8A8A";
    private const string DefTint = "#3300E000";
    private const string DefRing = "#2DA63B";
    private const string DefIcons = "#FEF7FF";

    public string Position { get; private set; } = "Bottom";
    public double UiScale => _uiScale;
    public double Glass { get; private set; } = 0.73; // pill background opacity
    public double Gloss { get; private set; } = 1.0;  // specular highlight multiplier

    private readonly Dictionary<string, string> _colors = new()
    {
        ["bg"] = DefBg, ["border"] = DefBorder, ["track"] = DefTrack,
        ["trackFill"] = DefTrackFill, ["tint"] = DefTint,
        ["ring"] = DefRing, ["icons"] = DefIcons,
    };

    public string GetColorHex(string key) => _colors.TryGetValue(key, out var v) ? v : "#FFFFFF";

    private static double GetDouble(System.Text.Json.JsonElement root, string name, double fallback)
    {
        try { return root.TryGetProperty(name, out var el) ? el.GetDouble() : fallback; }
        catch { return fallback; }
    }

    private static string GetString(System.Text.Json.JsonElement root, string name, string fallback)
    {
        try { return root.TryGetProperty(name, out var el) ? (el.GetString() ?? fallback) : fallback; }
        catch { return fallback; }
    }

    private void LoadSettings()
    {
        try
        {
            if (!System.IO.File.Exists(SettingsPath)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(SettingsPath));
            var root = doc.RootElement;
            _uiScale = Math.Clamp(GetDouble(root, "scale", _uiScale), 0.8, 2.5);
            string pos = GetString(root, "position", "Bottom");
            if (pos is "Top" or "Bottom" or "Left" or "Right") Position = pos;
            Glass = Math.Clamp(GetDouble(root, "glass", Glass), 0.4, 1.0);
            Gloss = Math.Clamp(GetDouble(root, "gloss", Gloss), 0.0, 1.5);
            _bgImageFile = GetString(root, "bgImage", "");
            if (root.TryGetProperty("bgOnly", out var boEl))
                try { BgOnly = boEl.GetBoolean(); } catch { }
            if (root.TryGetProperty("sliderFx", out var fxEl))
                try { SliderFx = fxEl.GetBoolean(); } catch { }
            if (root.TryGetProperty("colors", out var cols))
                foreach (var k in _colors.Keys.ToList())
                {
                    string v = GetString(cols, k, _colors[k]);
                    if (TryParseColor(v, out _)) _colors[k] = v;
                }
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath)!);
            var cols = string.Join(",", _colors.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\""));
            System.IO.File.WriteAllText(SettingsPath,
                $"{{\"scale\":{_uiScale:F3},\"position\":\"{Position}\",\"glass\":{Glass:F2},\"gloss\":{Gloss:F2}," +
                $"\"bgImage\":\"{_bgImageFile}\",\"bgOnly\":{(BgOnly ? "true" : "false")},\"sliderFx\":{(SliderFx ? "true" : "false")},\"colors\":{{{cols}}}}}");
        }
        catch { }
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        try { color = (Color)ColorConverter.ConvertFromString(hex); return true; }
        catch { color = Colors.White; return false; }
    }

    private void SetRes(string resKey, Color color)
        => Application.Current.Resources[resKey] = new SolidColorBrush(color);

    public void ApplyColors()
    {
        // Background takes its alpha from the glass slider; tint keeps its own alpha.
        // (Brushes live in Resources as new instances each time because App.xaml
        //  brushes are frozen by WPF and can't be mutated in place.)
        if (TryParseColor(_colors["bg"], out var bg))
            SetRes("PillBgBrush", Color.FromArgb((byte)(255 * Glass), bg.R, bg.G, bg.B));
        foreach (var (key, res) in new[] { ("border", "PillBorderBrush"), ("track", "TrackBgBrush"),
            ("trackFill", "TrackFillGrayBrush"), ("tint", "SpotifyTintBrush"),
            ("ring", "RingGreenBrush"), ("icons", "IconFillBrush") })
            if (TryParseColor(_colors[key], out var c))
                SetRes(res, c);
        GlossOverlay.Opacity = 0.55 * Gloss;
    }

    public bool SetColorHex(string key, string hex)
    {
        if (!_colors.ContainsKey(key) || !TryParseColor(hex, out _)) return false;
        _colors[key] = hex;
        ApplyColors();
        SaveSettings();
        return true;
    }

    public void SetGlass(double v)
    {
        Glass = Math.Clamp(v, 0.4, 1.0);
        ApplyColors();
        SaveSettings();
    }

    public void SetGloss(double v)
    {
        Gloss = Math.Clamp(v, 0.0, 1.5);
        ApplyColors();
        SaveSettings();
    }

    public void ResetAppearance()
    {
        _colors["bg"] = DefBg; _colors["border"] = DefBorder; _colors["track"] = DefTrack;
        _colors["trackFill"] = DefTrackFill; _colors["tint"] = DefTint;
        _colors["ring"] = DefRing; _colors["icons"] = DefIcons;
        Glass = 0.73; Gloss = 1.0;
        ApplyColors();
        ApplyPosition("Bottom");
        SaveSettings();
    }

    // ---------- Custom wallpaper (static image or animated GIF) ----------

    public void PickBackgroundImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            Title = "Choose pill wallpaper",
        };
        if (dlg.ShowDialog() != true) return;
        string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")) return;
        ImportWallpaperFile(dlg.FileName, ext);
    }

    /// <summary>Copies an image into the wallpaper slot and applies it.</summary>
    public void ImportWallpaperFile(string src, string ext)
    {
        try
        {
            ImageBehavior.SetAnimatedSource(BgImage, null);
            BgImage.Source = null;
            System.IO.Directory.CreateDirectory(AppDataDir);
            foreach (var f in System.IO.Directory.GetFiles(AppDataDir, "bg.*"))
                System.IO.File.Delete(f);
            string dst = System.IO.Path.Combine(AppDataDir, "bg" + ext);
            System.IO.File.Copy(src, dst, overwrite: true);
            SetWallpaperFile("bg" + ext);
            DebugLog.Write("wallpaper set: bg" + ext);
        }
        catch (Exception ex) { DebugLog.Write("wallpaper FAIL: " + ex.Message); }
    }

    public void SetWallpaperFile(string fileName)
    {
        _bgImageFile = fileName;
        ApplyBackground();
        SaveSettings();
    }

    public void ClearBackgroundImage()
    {
        _bgImageFile = "";
        try { foreach (var f in System.IO.Directory.GetFiles(AppDataDir, "bg.*")) System.IO.File.Delete(f); }
        catch { }
        HideWallpaper();
        SaveSettings();
    }

    private void HideWallpaper()
    {
        ImageBehavior.SetAnimatedSource(BgImage, null);
        BgImage.Source = null;
        BgClip.Visibility = Visibility.Collapsed;
        ApplyGlassLayers();
    }

    /// <summary>Glass on = solid color (+tint over wallpaper). Glass off =
    /// wallpaper only (border ring + gloss stay for definition).</summary>
    private void ApplyGlassLayers()
    {
        bool showOnly = BgOnly && BgClip.Visibility == Visibility.Visible;
        if (showOnly)
        {
            Pill.Background = Brushes.Transparent;
            GlassTint.Visibility = Visibility.Collapsed;
        }
        else
        {
            Pill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "PillBgBrush");
            GlassTint.Visibility = BgClip.Visibility;
        }
    }

    public void SetBgOnly(bool on)
    {
        BgOnly = on;
        ApplyGlassLayers();
        SaveSettings();
        DebugLog.Write("bgOnly=" + on);
    }

    private void ApplyBackground()
    {
        if (string.IsNullOrEmpty(_bgImageFile)) { HideWallpaper(); return; }
        string path = System.IO.Path.Combine(AppDataDir, _bgImageFile);
        if (!System.IO.File.Exists(path)) { _bgImageFile = ""; HideWallpaper(); return; }
        try
        {
            if (_bgImageFile.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                BgImage.Source = null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                ImageBehavior.SetAnimatedSource(BgImage, bmp);
                ImageBehavior.SetRepeatBehavior(BgImage, RepeatBehavior.Forever);
            }
            else
            {
                ImageBehavior.SetAnimatedSource(BgImage, null);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 872;
                bmp.EndInit();
                bmp.Freeze();
                BgImage.Source = bmp;
            }
            BgClip.Visibility = Visibility.Visible;
            ApplyGlassLayers(); // glass color tints over the art (unless bg-only mode)
            DebugLog.Write("wallpaper applied: " + _bgImageFile);
        }
        catch (Exception ex)
        {
            DebugLog.Write("wallpaper apply FAIL: " + ex.Message);
            _bgImageFile = "";
            HideWallpaper();
        }
    }

    // ---------- Animated slider shimmer ----------

    private void SetupShimmer()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        var stops = new[]
        {
            new GradientStop(Colors.Transparent, -0.5),
            new GradientStop(Color.FromArgb(0x55, 255, 255, 255), -0.25),
            new GradientStop(Colors.Transparent, 0.0),
        };
        foreach (var s in stops) brush.GradientStops.Add(s);
        SysShimmer.Background = brush;
        SpotShimmer.Background = brush;

        _shimmerStory = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        foreach (var s in stops)
        {
            var anim = new DoubleAnimation(s.Offset, s.Offset + 1.8, TimeSpan.FromSeconds(2.4));
            Storyboard.SetTarget(anim, s);
            Storyboard.SetTargetProperty(anim, new PropertyPath(GradientStop.OffsetProperty));
            _shimmerStory.Children.Add(anim);
        }
        ApplySliderFx();
    }

    public void SetSliderFx(bool on)
    {
        SliderFx = on;
        ApplySliderFx();
        SaveSettings();
    }

    private void ApplySliderFx()
    {
        if (_shimmerStory == null) return;
        if (SliderFx)
        {
            SysShimmer.Visibility = Visibility.Visible;
            SpotShimmer.Visibility = Visibility.Visible;
            _shimmerStory.Begin(this, true);
        }
        else
        {
            _shimmerStory.Pause(this);
            SysShimmer.Visibility = Visibility.Collapsed;
            SpotShimmer.Visibility = Visibility.Collapsed;
        }
    }

    public void ApplyScale(double s, bool save = true)
    {
        _uiScale = Math.Clamp(s, 0.8, 2.5);
        ApplyLayout();
        if (save) SaveSettings();
    }

    public void ApplyPosition(string pos, bool save = true)
    {
        if (pos is not ("Top" or "Bottom" or "Left" or "Right")) return;
        Position = pos;
        ApplyLayout();
        if (save) SaveSettings();
        DebugLog.Write($"position -> {pos}");
    }

    private bool IsVertical => Position == "Left" || Position == "Right";

    private void ApplyLayout()
    {
        // Shell swaps to vertical on side docks; content Viewbox rotates 90 deg.
        double w = FigmaW * _uiScale, h = FigmaH * _uiScale;
        bool vert = IsVertical;
        Pill.Width = vert ? h : w;
        Pill.Height = vert ? w : h;
        Pill.CornerRadius = new CornerRadius(h / 2);
        GlossOverlay.CornerRadius = new CornerRadius(h / 2);
        BgClip.CornerRadius = new CornerRadius(h / 2);
        GlassTint.CornerRadius = new CornerRadius(h / 2);
        // Border doesn't clip its child to rounded corners — clip the wallpaper manually.
        double cw = vert ? h : w, ch = vert ? w : h;
        BgClip.Clip = new RectangleGeometry(new Rect(0, 0, cw, ch), h / 2, h / 2);
        OsdViewBox.Width = w;
        OsdViewBox.Height = h;
        BgClip.CornerRadius = new CornerRadius(h / 2);
        GlassTint.CornerRadius = new CornerRadius(h / 2);
        double angle = Position == "Left" ? -90 : Position == "Right" ? 90 : 0;
        OsdViewBox.LayoutTransform = angle == 0 ? Transform.Identity : new RotateTransform(angle);
        // Counter-rotate the speaker glyphs so they stay upright on side docks.
        // (Chevrons intentionally rotate into ^/v: up=prev, down=next.)
        System.Windows.Media.Transform speakerT =
            angle == 0 ? Transform.Identity : new RotateTransform(-angle);
        SysSpeakerPath.RenderTransform = speakerT;
        SpotSpeakerPath.RenderTransform = speakerT;
        Width = (vert ? h : w) + 28;
        Height = (vert ? w : h) + 28;
        PositionOsd();
        DebugLog.Write($"ApplyLayout scale={_uiScale:F2} pos={Position} pill={Pill.Width:F0}x{Pill.Height:F0}");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            DebugLog.Write($"Loaded start. work={SystemParameters.WorkArea} size={Width}x{Height}");
            PositionOsd();
            await _spot.InitAsync();

            _hook.VolumeUp += () => Dispatcher.Invoke(() => { DebugLog.Write("hook VolumeUp"); _vol.ChangeBy(0.05f); ShowOsd(OsdMode.System); });
            _hook.VolumeDown += () => Dispatcher.Invoke(() => { DebugLog.Write("hook VolumeDown"); _vol.ChangeBy(-0.05f); ShowOsd(OsdMode.System); });
            _hook.MuteToggle += () => Dispatcher.Invoke(() => { DebugLog.Write("hook Mute"); _vol.ToggleMute(); ShowOsd(OsdMode.System); });
            bool hookOk = _hook.Start();
            DebugLog.Write($"hook Start ok={hookOk}");

            _vol.VolumeChanged += (v, muted) => Dispatcher.Invoke(() =>
            {
                DebugLog.Write($"VolumeChanged v={v:F2} muted={muted}");
                RefreshSystemTrack(v, muted);
                // Still show our pill even when volume was changed elsewhere (taskbar slider)
                if (Visibility != Visibility.Visible) ShowOsd(_mode);
            });

            _hideTimer.Tick += (_, _) => HideOsd();
            _spotTimer.Tick += async (_, _) => await RefreshSpotifyAsync();
            _spotTimer.Start();

            RefreshSystemTrack(_vol.GetVolume(), _vol.GetMute());
            DrawRing(SysRing, 36, 36, 28, 1.0);   // idle: full 315 deg
            DrawRing(SpotRing, 46, 46, 38, 1.0);

            LoadSettings();
            ApplyColors();
            ApplyLayout();
            SetupShimmer();
            ApplyBackground();
            _tray = new Services.VolumeTray(this);
            DebugLog.Write("tray ready");
            DebugLog.Write($"rescheck bg={TryFindResource("PillBgBrush") != null} pillbg={Pill.Background} pillvis={Pill.Visibility} winvis={Visibility} opacity={Pill.Opacity} wnd={Width}x{Height} at {Left:F0},{Top:F0}");

            // Show on launch and STAY 10s so you can't miss it (later popups use 1.8s).
            ShowOsd(OsdMode.System, stayMs: 10000);
            DebugLog.Write("Loaded done, pill should be visible for 10s");
        }
        catch (Exception ex) { DebugLog.Write("Loaded FAIL: " + ex); }
    }

    // ---------- OSD show / hide ----------

    private void PositionOsd()
    {
        var work = SystemParameters.WorkArea;
        const double margin = 24;
        if (!IsVertical)
        {
            Left = work.Left + (work.Width - Width) / 2;
            Top = Position == "Top" ? work.Top + margin : work.Bottom - Height - margin;
        }
        else
        {
            Top = work.Top + (work.Height - Height) / 2;
            Left = Position == "Left" ? work.Left + margin : work.Right - Width - margin;
        }
    }

    private void ShowOsd(OsdMode mode, int stayMs = 3000)
    {
        bool modeChanged = mode != _shownMode;
        bool wasHidden = Visibility != Visibility.Visible;
        _mode = mode;
        _shownMode = mode;
        PositionOsd();

        var incoming = mode == OsdMode.System ? SystemMode : SpotifyMode;
        var outgoing = mode == OsdMode.System ? SpotifyMode : SystemMode;
        var inTrans = mode == OsdMode.System ? SysTrans : SpotTrans;
        var outTrans = mode == OsdMode.System ? SpotTrans : SysTrans;

        if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
        // Re-assert topmost (Windows can drop it behind fullscreen apps)
        Topmost = false;
        Topmost = true;

        // Snap out of any in-flight transition first. WPF freezes animation
        // clocks while hidden, so hiding mid-fade (the 3s timer can fire inside
        // the 240ms slide) used to strand a frame at opacity 0 forever —
        // exactly the empty pill from your screenshot.
        SystemMode.BeginAnimation(UIElement.OpacityProperty, null);
        SpotifyMode.BeginAnimation(UIElement.OpacityProperty, null);
        SysTrans.BeginAnimation(TranslateTransform.XProperty, null);
        SysTrans.BeginAnimation(TranslateTransform.YProperty, null);
        SpotTrans.BeginAnimation(TranslateTransform.XProperty, null);
        SpotTrans.BeginAnimation(TranslateTransform.YProperty, null);
        SystemMode.Opacity = 1;
        SpotifyMode.Opacity = 1;
        SysTrans.X = 0; SysTrans.Y = 0;
        SpotTrans.X = 0; SpotTrans.Y = 0;

        int token = ++_transitionToken;
        if (modeChanged)
        {
            // Slide direction follows the layout: horizontal docks slide on X,
            // vertical (side) docks slide on Y.
            double dir = mode == OsdMode.Spotify ? -1 : 1;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slideProp = IsVertical ? TranslateTransform.YProperty : TranslateTransform.XProperty;
            incoming.Visibility = Visibility.Visible;

            // Outgoing frame: quick fade + drift out
            outgoing.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)));
            outTrans.BeginAnimation(slideProp,
                new DoubleAnimation(0, 30 * dir, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });

            // Incoming frame: slide in from the side + fade
            if (IsVertical) { inTrans.Y = -36 * dir; inTrans.X = 0; }
            else { inTrans.X = -36 * dir; inTrans.Y = 0; }
            incoming.Opacity = 0;
            incoming.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            inTrans.BeginAnimation(slideProp,
                new DoubleAnimation(-36 * dir, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });

            // Collapse the old frame after its fade (ignored if user toggled again)
            var collapse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
            collapse.Tick += (_, _) =>
            {
                collapse.Stop();
                if (token == _transitionToken && _shownMode == mode)
                {
                    outgoing.Visibility = Visibility.Collapsed;
                    outgoing.Opacity = 1;
                    outTrans.X = 0;
                    outTrans.Y = 0;
                }
            };
            collapse.Start();
            DebugLog.Write($"ShowOsd mode={mode} TRANSITION dir={dir}");
        }
        else
        {
            incoming.Visibility = Visibility.Visible;
            outgoing.Visibility = Visibility.Collapsed;
        }

        if (wasHidden)
        {
            // Subtle pop when the pill appears
            var pop = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(160))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(stayMs);
        _hideTimer.Start();
        // Full speed while visible: 2Hz Spotify poll.
        _spotTimer.Stop();
        _spotTimer.Interval = TimeSpan.FromMilliseconds(500);
        _spotTimer.Start();
        Services.EcoHelper.SetEco(false);
        DebugLog.Write($"ShowOsd mode={mode} at Left={Left:F0} Top={Top:F0} stay={stayMs}ms");
    }

    private void HideOsd()
    {
        // Never hide under an interacting user: hovering or mid-drag holds the pill.
        if (Pill.IsMouseOver || SysTrack.IsMouseCaptured || SpotTrack.IsMouseCaptured)
        {
            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(500);
            _hideTimer.Start();
            return;
        }
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(3000);
        Visibility = Visibility.Hidden;
        // Idle: slow the Spotify poll to 0.5Hz, throttle CPU (Eco leaf), trim RAM.
        _spotTimer.Stop();
        _spotTimer.Interval = TimeSpan.FromMilliseconds(2000);
        _spotTimer.Start();
        Services.EcoHelper.SetEco(true);
        double wsMb = Services.EcoHelper.Trim();
        DebugLog.Write($"HideOsd eco on, WS={wsMb:F1}MB");
    }

    private void Pill_MouseEnter(object sender, MouseEventArgs e)
    {
        _hideTimer.Stop();
        DebugLog.Write("hover enter -> hold visible");
    }

    private void Pill_MouseLeave(object sender, MouseEventArgs e)
    {
        // A drag in flight (mouse captured) keeps the pill; the Up handler resumes the timer.
        if (SysTrack.IsMouseCaptured || SpotTrack.IsMouseCaptured) return;
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(3000);
        _hideTimer.Start();
        DebugLog.Write("hover leave -> hide in 3s");
    }

    private void RestartHideAfterPointer()
    {
        // Called on drag release: if the cursor was dropped outside, no Leave
        // event will come, so resume the timer here.
        if (Pill.IsMouseOver) return;
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(3000);
        _hideTimer.Start();
    }

    private void Pill_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Double-click on empty pill area opens settings (not on buttons/tracks).
        if (e.ClickCount == 2 && (e.OriginalSource == Pill || e.OriginalSource == GlossOverlay))
            OpenSettings();
    }

    public void OpenSettings()
    {
        // Never let a settings bug take down the pill.
        try
        {
            if (_settings == null)
            {
                _settings = new SettingsWindow(this);
                _settings.Closed += (_, _) => _settings = null;
            }
            _settings.Show();
            _settings.Activate();
            DebugLog.Write("settings opened");
        }
        catch (Exception ex) { DebugLog.Write("settings open FAIL: " + ex.Message); }
    }

    private void SpeakerBtn_Click(object sender, RoutedEventArgs e)
    {
        // Spec interaction: clicking transport/speaker area toggles Frame1 <-> Frame2
        ShowOsd(_mode == OsdMode.System ? OsdMode.Spotify : OsdMode.System);
        _ = RefreshSpotifyAsync();
    }

    // ---------- System track ----------

    private void RefreshSystemTrack(float v, bool muted)
    {
        double w = 564 * (muted ? 0 : v);
        SysFill.Width = Math.Clamp(w, 0, 564);
    }

    private void SysTrack_Down(object sender, MouseButtonEventArgs e)
    {
        SysTrack.CaptureMouse();
        SetSystemVolumeFromPointer(e.GetPosition(SysTrack));
        e.Handled = true;
    }

    private void SysTrack_Move(object sender, MouseEventArgs e)
    {
        if (!SysTrack.IsMouseCaptured) return;
        if (e.LeftButton != MouseButtonState.Pressed) { SysTrack.ReleaseMouseCapture(); return; }
        SetSystemVolumeFromPointer(e.GetPosition(SysTrack));
    }

    private void SysTrack_Up(object sender, MouseButtonEventArgs e)
    {
        SysTrack.ReleaseMouseCapture();
        RestartHideAfterPointer();
    }

    private void SetSystemVolumeFromPointer(Point pos)
    {
        // Side docks are rotated: top of the bar = max.
        float ratio = IsVertical
            ? (float)(1 - pos.Y / SysTrack.ActualHeight)
            : (float)(pos.X / SysTrack.ActualWidth);
        ratio = Math.Clamp(ratio, 0f, 1f);
        _vol.SetVolume(ratio);
        RefreshSystemTrack(ratio, false);
        ShowOsd(OsdMode.System);
    }

    // ---------- Spotify ----------

    private async Task RefreshSpotifyAsync()
    {
        var st = await _spot.GetStateAsync();
        _spotRunning = st.Running;
        _hasSession = st.HasSession;
        if (!string.IsNullOrEmpty(st.Title)) { _spotTitle = st.Title; _spotArtist = st.Artist; }

        // Ring + tooltip reflect session presence: dim ring when nothing to play.
        double ringOpacity = st.HasSession ? 1.0 : 0.35;
        SysRing.Opacity = ringOpacity;
        SpotRing.Opacity = ringOpacity;
        UpdateSpotifyTip();

        // Spotify gone while we're hidden: silently fall back so the next
        // summon shows System instead of an empty Spotify frame.
        if (!st.Running && !st.HasSession && _shownMode == OsdMode.Spotify && Visibility != Visibility.Visible)
        {
            _mode = OsdMode.System;
            _shownMode = OsdMode.System;
            SystemMode.Visibility = Visibility.Visible;
            SpotifyMode.Visibility = Visibility.Collapsed;
            SystemMode.Opacity = 1;
            SpotifyMode.Opacity = 1;
            SysTrans.X = 0;
            SpotTrans.X = 0;
            SysTrans.Y = 0;
            SpotTrans.Y = 0;
        }

        // Track bar = per-app Spotify volume (green-tinted bar on right).
        // Don't fight the user while they're dragging (mouse captured by track).
        if (!SpotTrack.IsMouseCaptured)
            SpotFill.Width = Math.Clamp(508 * st.AppVolume, 0, 508);

        // Ring = playback progress (0..315 deg)
        double progress = st.DurationSec > 1 ? st.PositionSec / st.DurationSec : 1.0;
        progress = Math.Clamp(progress, 0, 1);
        DrawRing(SpotRing, 46, 46, 38, progress);
        DrawRing(SysRing, 36, 36, 28, progress);

        // Album art: refetch only when the track changes, clear when session ends.
        if (st.HasSession && !string.IsNullOrEmpty(st.Title))
        {
            string key = $"{st.Artist}\n{st.Title}";
            if (key != _artKey) { _artKey = key; _ = LoadArtAsync(key); }
        }
        else if (_artKey != "")
        {
            _artKey = "";
            ClearArt();
        }

        // Auto-switch feel: new track while playing pops the Spotify frame
        if (st.Playing && !string.IsNullOrEmpty(st.Title) && st.Title != _lastTitle)
        {
            _lastTitle = st.Title;
            if (Visibility != Visibility.Visible)
                ShowOsd(OsdMode.Spotify);
            else if (_mode == OsdMode.Spotify)
            {
                _hideTimer.Stop();
                _hideTimer.Start();
            }
        }
    }

    private async Task LoadArtAsync(string key)
    {
        try
        {
            var session = await _spot.GetSpotifySessionAsync();
            if (session == null || key != _artKey) return;
            var props = await session.TryGetMediaPropertiesAsync();
            if (key != _artKey) return;
            if (props.Thumbnail == null)
            {
                Dispatcher.Invoke(() => { if (key == _artKey) ClearArt(); });
                return;
            }
            using var ras = await props.Thumbnail.OpenReadAsync();
            using var ms = new MemoryStream();
            await ras.AsStreamForRead().CopyToAsync(ms);
            if (key != _artKey) return;
            ms.Seek(0, SeekOrigin.Begin);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 128;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            Dispatcher.Invoke(() =>
            {
                if (key != _artKey) return;
                SysArtBrush.ImageSource = bmp;
                SpotArtBrush.ImageSource = bmp;
                SysArt.Visibility = Visibility.Visible;
                SpotArt.Visibility = Visibility.Visible;
                SysArtDim.Visibility = Visibility.Visible;
                SpotArtDim.Visibility = Visibility.Visible;
                DebugLog.Write("art loaded for track");
            });
        }
        catch (Exception ex) { DebugLog.Write("art FAIL: " + ex.Message); }
    }

    private void ClearArt()
    {
        SysArtBrush.ImageSource = null;
        SpotArtBrush.ImageSource = null;
        SysArt.Visibility = Visibility.Collapsed;
        SpotArt.Visibility = Visibility.Collapsed;
        SysArtDim.Visibility = Visibility.Collapsed;
        SpotArtDim.Visibility = Visibility.Collapsed;
    }

    private void SpotTrack_Down(object sender, MouseButtonEventArgs e)
    {
        SpotTrack.CaptureMouse();
        SetSpotifyVolumeFromPointer(e.GetPosition(SpotTrack));
        e.Handled = true;
    }

    private void SpotTrack_Move(object sender, MouseEventArgs e)
    {
        if (!SpotTrack.IsMouseCaptured) return;
        if (e.LeftButton != MouseButtonState.Pressed) { SpotTrack.ReleaseMouseCapture(); return; }
        SetSpotifyVolumeFromPointer(e.GetPosition(SpotTrack));
    }

    private void SpotTrack_Up(object sender, MouseButtonEventArgs e)
    {
        SpotTrack.ReleaseMouseCapture();
        RestartHideAfterPointer();
    }

    private void SetSpotifyVolumeFromPointer(Point pos)
    {
        float ratio = IsVertical
            ? (float)(1 - pos.Y / SpotTrack.ActualHeight)
            : (float)(pos.X / SpotTrack.ActualWidth);
        ratio = Math.Clamp(ratio, 0f, 1f);
        _spot.SetSpotifyAppVolume(ratio);
        SpotFill.Width = Math.Clamp(508 * ratio, 0, 508);
        ShowOsd(OsdMode.Spotify);
    }

    private async void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_hasSession || _spotRunning)
        {
            await _spot.TogglePlayPauseAsync();
            ShowOsd(OsdMode.Spotify);
            await RefreshSpotifyAsync();
        }
        else
        {
            _vol.ToggleMute();
            ShowOsd(OsdMode.System);
        }
    }

    private async void NextBtn_Click(object sender, RoutedEventArgs e) => await NextTrackAsync();
    private async void PrevBtn_Click(object sender, RoutedEventArgs e) => await PrevTrackAsync();

    private async Task NextTrackAsync()
    {
        if (!_hasSession && !_spotRunning) { ShowOsd(OsdMode.System); return; }
        await _spot.NextAsync();
        ShowOsd(OsdMode.Spotify);
    }

    private async Task PrevTrackAsync()
    {
        if (!_hasSession && !_spotRunning) { ShowOsd(OsdMode.System); return; }
        await _spot.PrevAsync();
        ShowOsd(OsdMode.Spotify);
    }

    /// <summary>Nudges whichever slider is active (system master or Spotify app volume).</summary>
    private void AdjustActiveSlider(float delta)
    {
        if (_mode == OsdMode.Spotify)
        {
            float v = Math.Clamp(_spot.GetSpotifyAppVolume() + delta, 0f, 1f);
            _spot.SetSpotifyAppVolume(v);
            SpotFill.Width = Math.Clamp(508 * v, 0, 508);
            ShowOsd(OsdMode.Spotify);
        }
        else
        {
            _vol.ChangeBy(delta);
            ShowOsd(OsdMode.System);
        }
        DebugLog.Write($"slider nudge {delta:+0.00;-0.00} on {_mode}");
    }

    // ---------- Ring: 315 deg arc with 45 deg gap at 6 o'clock ----------
    // Start = 112.5 deg (gap edge), sweep = progress * 315 deg clockwise.

    private static void DrawRing(System.Windows.Shapes.Path path, double cx, double cy, double r, double progress)
    {
        const double startDeg = 112.5;
        double sweep = Math.Clamp(progress, 0, 1) * 315.0;
        if (sweep < 0.5)
        {
            path.Data = null;
            return;
        }
        double endDeg = startDeg + sweep;
        Point p0 = Polar(cx, cy, r, startDeg);
        Point p1 = Polar(cx, cy, r, endDeg);
        bool large = sweep > 180;
        var fig = new PathFigure { StartPoint = p0, IsClosed = false };
        fig.Segments.Add(new ArcSegment(p1, new Size(r, r), 0, large, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        path.Data = geo;
    }

    private static Point Polar(double cx, double cy, double r, double degFrom3OClock)
    {
        double rad = degFrom3OClock * Math.PI / 180.0;
        return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(hwnd, HOTKEY_ID);
                UnregisterHotKey(hwnd, HOTKEY_BIGGER);
                UnregisterHotKey(hwnd, HOTKEY_SMALLER);
                UnregisterHotKey(hwnd, HOTKEY_SETTINGS);
                UnregisterHotKey(hwnd, HOTKEY_ALTX);
                UnregisterHotKey(hwnd, HOTKEY_ALTS);
                UnregisterHotKey(hwnd, HOTKEY_ALTZ);
                UnregisterHotKey(hwnd, HOTKEY_ALTC);
                UnregisterHotKey(hwnd, HOTKEY_ALTSHIFT_Z);
                UnregisterHotKey(hwnd, HOTKEY_ALTSHIFT_C);
                UnregisterHotKey(hwnd, HOTKEY_ALTSHIFT_A);
                UnregisterHotKey(hwnd, HOTKEY_ALTSHIFT_D);
            }
        }
        catch { }
        _tray?.Dispose();
        _settings?.Close();
        _hook.Dispose();
        _vol.Dispose();
        _spotTimer.Stop();
        _hideTimer.Stop();
        base.OnClosed(e);
    }
}
