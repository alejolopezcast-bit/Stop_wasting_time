using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using StopWastingTime.App.Infrastructure;
using StopWastingTime.App.Localization;
using StopWastingTime.App.ViewModels;

namespace StopWastingTime.App.Views;

/// <summary>
/// The app proper: the screens, the tray icon, and the rules about closing. Closing the window only
/// sends the app to the tray, because a blocker that stops blocking the moment the window is in the way
/// is not a blocker. Leaving for real is the Exit entry in the tray menu, and a strict or ultra session
/// refuses even that until the time is up.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly Localizer _localizer;
    private readonly ILogger<MainWindow> _logger;

    private TaskbarIcon? _trayIcon;
    private bool _isExiting;
    private bool _explainedTheTray;

    public MainWindow(ShellViewModel shell, Localizer localizer, ILogger<MainWindow> logger)
    {
        _shell = shell;
        _localizer = localizer;
        _logger = logger;
        DataContext = shell;

        InitializeComponent();

        // With a custom caption, maximising has to be told where the taskbar is.
        MaximizeBehaviour.Apply(this);

        Loaded += async (_, _) =>
        {
            CreateTrayIcon();
            await _shell.InitializeAsync();
        };
    }

    /// <summary>
    /// The app keeps working from the tray while a session runs, because the blocking has to outlive the
    /// window being in the way.
    /// </summary>
    private void CreateTrayIcon()
    {
        var menu = new ContextMenu();

        // Bound rather than assigned, so the menu follows the language picker like the rest of the app.
        var show = new MenuItem();
        show.SetBinding(HeaderedItemsControl.HeaderProperty, Localizer.Bind("Tray_Show"));
        show.Click += (_, _) => RestoreWindow();
        menu.Items.Add(show);

        var exit = new MenuItem();
        exit.SetBinding(HeaderedItemsControl.HeaderProperty, Localizer.Bind("Common_Exit"));
        exit.Click += (_, _) => TryExit();
        menu.Items.Add(exit);

        _trayIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")),
            ToolTipText = _shell.TrayTooltip,
            ContextMenu = menu,
            DataContext = _shell
        };

        _trayIcon.TrayLeftMouseUp += (_, _) => RestoreWindow();

        // A tray icon built in code is never added to a visual tree, so nothing creates it. Without this
        // the icon simply does not appear, and since closing the window hides the app, that would leave
        // it running with no way back.
        _trayIcon.ForceCreate();

        _shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(ShellViewModel.TrayTooltip))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (_trayIcon is not null)
                {
                    _trayIcon.ToolTipText = _shell.TrayTooltip;
                }
            });
        };
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>The only way out, and it is refused while a session has promised otherwise.</summary>
    private void TryExit()
    {
        if (_shell.IsStrictSessionRunning)
        {
            RestoreWindow();
            MessageBox.Show(
                this,
                _localizer["Tray_StrictNoExit"],
                "Stop Wasting Time",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _isExiting = true;
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isExiting)
        {
            base.OnClosing(e);
            return;
        }

        // Closing the window never ends the app: it goes to the tray and keeps working. During a session
        // that is what keeps the blocking alive, and outside one it keeps the app a click away.
        e.Cancel = true;
        Hide();

        if (_shell.IsSessionRunning)
        {
            Notify(_localizer["Tray_StillRunning"]);
            return;
        }

        if (!_explainedTheTray)
        {
            // Said once: after that, someone who closes the window knows where it went.
            Notify(_localizer["Tray_Explain"]);
        }
    }

    /// <summary>
    /// A balloon tip, if Windows is willing. Notifications are suppressed by focus assist and by policy,
    /// and none of that is a reason to stop the window from closing.
    /// </summary>
    private void Notify(string message)
    {
        _explainedTheTray = true;

        try
        {
            _trayIcon?.ShowNotification("Stop Wasting Time", message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not show the tray notification.");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        base.OnClosed(e);
    }
}
