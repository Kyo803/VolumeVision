using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace VolumeOSD.Controls;

/// <summary>One timeline cell: frame index, ruler tick label, thumbnail.</summary>
public sealed class TimelineItem
{
    public int Index { get; init; }
    public string Tick { get; init; } = "";
    public BitmapImage? Thumb { get; init; }
}

/// <summary>
/// Animation timeline: transport controls, frame ruler and selectable frame cells.
/// The host owns the frames; this control only presents them and raises intent.
/// </summary>
public partial class Timeline : UserControl
{
    public event Action<int>? FrameSelected;
    public event Action? AddRequested;
    public event Action? DuplicateRequested;
    public event Action? DeleteRequested;
    public event Action? UndoRequested;
    public event Action? PlayToggled;
    public event Action? StopRequested;
    public event Action<int>? StepBy;

    public Timeline()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => Cells.ItemsSource;
        set
        {
            Cells.ItemsSource = value;
            Ruler.ItemsSource = value;
        }
    }

    public int SelectedIndex
    {
        get => Cells.SelectedIndex;
        set => Cells.SelectedIndex = value;
    }

    public void SetPlaying(bool playing) => PlayBtn.Content = playing ? "❚❚" : "▶";

    public void SetInfo(string text) => InfoText.Text = text;

    public void SetFrameLabel(string text) => FrameLabel.Text = text;

    public void SetLoopVisible(bool visible) => StopBtn.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void Cells_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Cells.SelectedIndex >= 0) FrameSelected?.Invoke(Cells.SelectedIndex);
    }

    private void Play_Click(object sender, RoutedEventArgs e) => PlayToggled?.Invoke();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void First_Click(object sender, RoutedEventArgs e) => StepBy?.Invoke(int.MinValue);
    private void Last_Click(object sender, RoutedEventArgs e) => StepBy?.Invoke(int.MaxValue);
    private void Prev_Click(object sender, RoutedEventArgs e) => StepBy?.Invoke(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => StepBy?.Invoke(1);
    private void Add_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke();
    private void Dupe_Click(object sender, RoutedEventArgs e) => DuplicateRequested?.Invoke();
    private void Del_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke();
    private void Undo_Click(object sender, RoutedEventArgs e) => UndoRequested?.Invoke();
}
