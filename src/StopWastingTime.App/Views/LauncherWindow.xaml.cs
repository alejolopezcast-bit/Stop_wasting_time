using System.Diagnostics;
using System.Windows;
using StopWastingTime.App.Infrastructure;
using StopWastingTime.Core;

namespace StopWastingTime.App.Views;

/// <summary>
/// The front door: the logo, the name, what the app managed to set up, and one button that opens it.
/// When the app is running without administrator rights the same button restarts it elevated, because
/// that is the only way the site blocking can work.
/// </summary>
public partial class LauncherWindow : Window
{
    private readonly StartupReport _report;
    private readonly Func<MainWindow> _mainWindowFactory;

    private bool _handedOver;

    public LauncherWindow(StartupReport report, Func<MainWindow> mainWindowFactory)
    {
        _report = report;
        _mainWindowFactory = mainWindowFactory;
        DataContext = report;

        InitializeComponent();
    }

    private void OnPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        if (_report.CanRelaunchElevated)
        {
            RelaunchElevated();
            return;
        }

        OpenApp();
    }

    private void OnOpenAnywayClick(object sender, RoutedEventArgs e) => OpenApp();

    /// <summary>Hands the session over to the main window and steps out of the way.</summary>
    private void OpenApp()
    {
        var window = _mainWindowFactory();
        Application.Current.MainWindow = window;
        window.Show();

        _handedOver = true;
        Close();
    }

    /// <summary>
    /// Starts the manifested executable, which makes Windows ask for administrator rights, and closes
    /// this copy so the two never fight over the same data.
    /// </summary>
    private void RelaunchElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _report.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas"
            });

            _handedOver = true;
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            // The usual case is the user saying no to the UAC prompt, which is a decision, not a fault.
            ShowError($"No se pudo abrir como administrador: {exception.Message}");
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureDataDirectory();

        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.DataDirectory,
            UseShellExecute = true
        });
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Closing the launcher without opening the app means the user is done.
        if (!_handedOver)
        {
            Application.Current.Shutdown();
        }
    }
}
