using CommunityToolkit.Mvvm.ComponentModel;
using StopWastingTime.App.Infrastructure;
using StopWastingTime.App.Localization;

namespace StopWastingTime.App.ViewModels;

/// <summary>
/// The launcher's words for the <see cref="StartupReport"/>. Every sentence is worked out when asked
/// for, so a new language only means asking again.
/// </summary>
public sealed class LauncherViewModel : ObservableObject, IDisposable
{
    private readonly Localizer _localizer;

    public LauncherViewModel(StartupReport report, Localizer localizer)
    {
        Report = report;
        _localizer = localizer;
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    public StartupReport Report { get; }

    public bool IsElevated => Report.IsElevated;

    public bool CanRelaunchElevated => Report.CanRelaunchElevated;

    public string DatabasePath => Report.DatabasePath;

    public string ElevationText => _localizer[IsElevated ? "Launcher_Admin_Yes" : "Launcher_Admin_No"];

    public string RulesText => _localizer.Format(
        "Launcher_Rules",
        _localizer.Plural("Count_Apps", Report.AppRules),
        _localizer.Plural("Count_Sites", Report.SiteRules));

    public string VersionText => _localizer.Format("Launcher_Version", Report.Version);

    public string PrimaryActionText => _localizer[CanRelaunchElevated ? "Launcher_OpenElevated" : "Launcher_Open"];

    /// <summary>The launcher closes long before the app does; it should not stay subscribed.</summary>
    public void Dispose() => _localizer.LanguageChanged -= OnLanguageChanged;

    // An empty name tells WPF that everything changed.
    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
