using System.Windows;
using System.Windows.Controls;

namespace StopWastingTime.App.Controls;

/// <summary>
/// The window caption, drawn by the app instead of by Windows. The system one is light and sits on top
/// of a dark interface like a sticker; this one belongs to it.
/// </summary>
public partial class TitleBar : UserControl
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status),
        typeof(object),
        typeof(TitleBar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CanMaximizeProperty = DependencyProperty.Register(
        nameof(CanMaximize),
        typeof(bool),
        typeof(TitleBar),
        new PropertyMetadata(true, OnCanMaximizeChanged));

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Optional content shown in the middle of the caption, such as a running session.</summary>
    public object? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public bool CanMaximize
    {
        get => (bool)GetValue(CanMaximizeProperty);
        set => SetValue(CanMaximizeProperty, value);
    }

    private Window? Host => Window.GetWindow(this);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MaximizeButton.Visibility = CanMaximize ? Visibility.Visible : Visibility.Collapsed;

        if (Host is { } window)
        {
            window.StateChanged += (_, _) => UpdateMaximizeIcon();
            UpdateMaximizeIcon();
        }
    }

    private static void OnCanMaximizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TitleBar)d).MaximizeButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The glyph has to say what the click will do, not what the window currently is.</summary>
    private void UpdateMaximizeIcon()
    {
        if (Host is not { } window)
        {
            return;
        }

        var maximized = window.WindowState == WindowState.Maximized;

        MaximizeIcon.Data = (System.Windows.Media.Geometry)FindResource(maximized ? "IconRestore" : "IconMaximize");
        MaximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (Host is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        if (Host is not { } window)
        {
            return;
        }

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Host?.Close();
}
