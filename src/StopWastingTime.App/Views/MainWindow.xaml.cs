using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using StopWastingTime.App.ViewModels;

namespace StopWastingTime.App.Views;

/// <summary>
/// The app proper: the three screens, the tray icon, and the rules about closing. A running session
/// sends the window to the tray instead of exiting, and a strict session refuses to let go at all.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private TaskbarIcon? _trayIcon;

    public MainWindow(ShellViewModel shell)
    {
        _shell = shell;
        DataContext = shell;

        InitializeComponent();

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

        var show = new MenuItem { Header = "Mostrar" };
        show.Click += (_, _) => RestoreWindow();
        menu.Items.Add(show);

        var exit = new MenuItem { Header = "Salir" };
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

    private void TryExit()
    {
        if (_shell.IsStrictSessionRunning)
        {
            RestoreWindow();
            MessageBox.Show(
                this,
                "Hay una sesión estricta en curso. La app no se puede cerrar hasta que termine.",
                "Stop Wasting Time",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_shell.IsStrictSessionRunning)
        {
            e.Cancel = true;
            MessageBox.Show(
                this,
                "Elegiste una sesión estricta: la app no se cierra hasta que termine el tiempo.",
                "Stop Wasting Time",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_shell.IsSessionRunning)
        {
            // An ordinary session keeps running in the tray: closing the window should not quietly
            // unblock everything.
            e.Cancel = true;
            Hide();
            _trayIcon?.ShowNotification(
                "Stop Wasting Time",
                "La sesión sigue corriendo. La app queda en la bandeja del sistema.");
            return;
        }

        base.OnClosing(e);
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        base.OnClosed(e);
    }
}
