using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StopWastingTime.App.Localization;

namespace StopWastingTime.App.Views;

/// <summary>
/// The small notice in the corner when something is closed for you. It never takes focus and fades out
/// on its own: during a focus session the last thing wanted is another window to deal with.
/// </summary>
public partial class BlockToastWindow : Window
{
    private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(4);

    /// <summary>Gap between the notice and the edge of the screen.</summary>
    private const double ScreenMargin = 24;

    public BlockToastWindow(string displayName, string? remaining, Localizer localizer)
    {
        InitializeComponent();

        DetailText.Text = remaining is null
            ? localizer.Format("Toast_Closed", displayName)
            : localizer.Format("Toast_ClosedWithTime", displayName, remaining);

        Loaded += OnLoaded;
    }

    /// <summary>Shows a notice above the tray area, then lets it fade away.</summary>
    public static void ShowFor(string displayName, string? remaining, Localizer localizer)
    {
        var toast = new BlockToastWindow(displayName, remaining, localizer);
        toast.Show();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var workingArea = SystemParameters.WorkArea;
        Left = workingArea.Right - Width - ScreenMargin;
        Top = workingArea.Bottom - ActualHeight - ScreenMargin;

        var timer = new DispatcherTimer { Interval = VisibleFor };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            FadeOut();
        };
        timer.Start();

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    private void FadeOut()
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
