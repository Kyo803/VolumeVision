using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WpfAnimatedGif;

namespace VolumeOSD;

/// <summary>
/// A library of wallpaper art: every GIF/image in %AppData%\VolumeOSD\gifs is
/// shown as its own animated pill preview. Click a pill to apply it.
/// </summary>
public partial class GifLibrary : Window
{
    private readonly MainWindow _main;

    private static string LibraryDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VolumeOSD", "gifs");

    private static readonly string[] Exts = { ".gif", ".png", ".jpg", ".jpeg", ".bmp" };

    public GifLibrary(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        FolderHint.Text = LibraryDir;
        LoadLibrary();

        // Smooth entrance
        Opacity = 0;
        ShellScale.ScaleX = ShellScale.ScaleY = 0.96;
        Loaded += (_, _) =>
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            var grow = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
            ShellScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            ShellScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        };
    }

    private void Shell_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- Gallery ----------

    private void LoadLibrary()
    {
        Items.Children.Clear();
        try
        {
            Directory.CreateDirectory(LibraryDir);
            var files = Directory.GetFiles(LibraryDir)
                .Where(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => Path.GetFileName(f))
                .Take(80)
                .ToList();

            if (files.Count == 0)
            {
                Items.Children.Add(new TextBlock
                {
                    Text = "No art yet — drop GIFs/PNGs into the folder above,\n" +
                           "or use “Add GIFs…” to copy them in.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x70)),
                    FontSize = 13,
                    Margin = new Thickness(4, 20, 0, 0),
                });
                StatusText.Text = "0 items";
                return;
            }

            foreach (var f in files)
                Items.Children.Add(BuildCard(f));

            StatusText.Text = $"{files.Count} item(s) · click a pill to apply";
        }
        catch (Exception ex) { StatusText.Text = "Could not read library: " + ex.Message; }
    }

    private FrameworkElement BuildCard(string path)
    {
        string name = Path.GetFileName(path);
        bool active = string.Equals(name, _main.BgSourceName, StringComparison.OrdinalIgnoreCase);

        var img = new Image { Stretch = Stretch.UniformToFill };
        try
        {
            if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                // No CacheOption.OnLoad: the GIF decoder must stream frames.
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                ImageBehavior.SetAnimatedSource(img, bmp);
                ImageBehavior.SetRepeatBehavior(img, RepeatBehavior.Forever);
            }
            else
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 300;
                bmp.EndInit();
                bmp.Freeze();
                img.Source = bmp;
            }
        }
        catch { }

        var clip = new RectangleGeometry(new Rect(0, 0, 288, 63), 31.5, 31.5);

        var pill = new Border
        {
            Width = 288,
            Height = 63,
            CornerRadius = new CornerRadius(31.5),
            Background = new SolidColorBrush(Color.FromArgb(0xBA, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(active ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x2C, 0x2C, 0x33)),
            BorderThickness = new Thickness(active ? 2 : 1),
            Clip = clip,
            Cursor = Cursors.Hand,
            Child = img,
        };

        var label = new TextBlock
        {
            Text = active ? name + "  ✓" : name,
            Foreground = new SolidColorBrush(active ? Color.FromRgb(0xF4, 0xF4, 0xF7) : Color.FromRgb(0x9A, 0x9A, 0xA5)),
            FontSize = 11,
            Margin = new Thickness(4, 6, 4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 288,
        };

        var card = new StackPanel { Margin = new Thickness(4, 4, 12, 10) };
        card.Children.Add(pill);
        card.Children.Add(label);
        pill.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Apply(path, name);
        };
        return card;
    }

    private void Apply(string path, string name)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            _main.ImportWallpaperFile(path, ext, name);
            StatusText.Text = $"Applied: {name}";
            LoadLibrary(); // refresh highlight
        }
        catch (Exception ex) { StatusText.Text = "Apply failed: " + ex.Message; }
    }

    // ---------- Toolbar ----------

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.gif;*.png;*.jpg;*.jpeg;*.bmp|GIF|*.gif|All files|*.*",
            Title = "Add art to the library",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            Directory.CreateDirectory(LibraryDir);
            int n = 0;
            foreach (var f in dlg.FileNames)
            {
                string dst = Path.Combine(LibraryDir, Path.GetFileName(f));
                if (!File.Exists(dst)) { File.Copy(f, dst); n++; }
            }
            StatusText.Text = $"Added {n} file(s).";
            LoadLibrary();
        }
        catch (Exception ex) { StatusText.Text = "Add failed: " + ex.Message; }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(LibraryDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LibraryDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText.Text = "Open folder failed: " + ex.Message; }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadLibrary();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _main.ClearBackgroundImage();
        StatusText.Text = "Wallpaper cleared.";
        LoadLibrary();
    }
}
