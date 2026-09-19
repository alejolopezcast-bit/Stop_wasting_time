using System.Diagnostics;
using System.Windows;
using StopWastingTime.Core;

namespace StopWastingTime.App;

/// <summary>
/// Startup screen. It reports what the launcher set up, so a launch that half worked is obvious rather
/// than silent. The focus, blocklist and statistics screens replace this content later.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

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
}
