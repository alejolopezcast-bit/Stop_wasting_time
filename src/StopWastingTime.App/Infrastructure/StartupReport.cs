using System.Globalization;
using System.IO;

namespace StopWastingTime.App.Infrastructure;

/// <summary>
/// What the launcher managed to set up before showing its window. The launcher screen displays it so a
/// failed launch is visible at a glance instead of being a window that opens and does nothing.
/// </summary>
public sealed record StartupReport(
    string Version,
    bool IsElevated,
    string DatabasePath,
    string LogPath,
    string ExecutablePath,
    int AppRules,
    int SiteRules)
{
    public string ElevationText => IsElevated
        ? "Sí, puede bloquear apps y sitios"
        : "No, el bloqueo de sitios no va a funcionar";

    public string RulesText => string.Format(
        CultureInfo.CurrentCulture,
        "{0} {1} y {2} {3} en la lista",
        AppRules,
        AppRules == 1 ? "app" : "apps",
        SiteRules,
        SiteRules == 1 ? "sitio" : "sitios");

    public string VersionText => $"Versión {Version}";

    /// <summary>
    /// True when the app is running without administrator rights but the elevated executable is sitting
    /// right there, so the launcher can offer to start it properly.
    /// </summary>
    public bool CanRelaunchElevated => !IsElevated && File.Exists(ExecutablePath);

    public string PrimaryActionText => CanRelaunchElevated
        ? "Abrir como administrador"
        : "Abrir Stop Wasting Time";
}
