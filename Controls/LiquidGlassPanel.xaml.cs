using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VolumeOSD.Controls;

public partial class LiquidGlassPanel : UserControl
{
    public static readonly DependencyProperty ContentMarginProperty =
        DependencyProperty.Register(nameof(ContentMargin), typeof(Thickness), typeof(LiquidGlassPanel),
            new PropertyMetadata(new Thickness(16)));

    public Thickness ContentMargin
    {
        get => (Thickness)GetValue(ContentMarginProperty);
        set => SetValue(ContentMarginProperty, value);
    }

    public LiquidGlassPanel()
    {
        InitializeComponent();
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => GlowLayer.Opacity = 0;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(Root);
        if (Root.ActualWidth <= 0 || Root.ActualHeight <= 0) return;
        var brush = (RadialGradientBrush)FindResource("CursorGlow");
        var origin = new Point(pos.X / Root.ActualWidth, pos.Y / Root.ActualHeight);
        brush.GradientOrigin = origin;
        brush.Center = origin;
        GlowLayer.Opacity = 0.9;
    }
}
