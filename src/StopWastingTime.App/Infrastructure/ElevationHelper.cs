using System.Security.Principal;

namespace StopWastingTime.App.Infrastructure;

/// <summary>
/// Tells whether the process is running elevated. The manifest asks Windows for administrator rights,
/// but a developer running the DLL through <c>dotnet</c> can still end up without them, and the UI says
/// so instead of failing silently when it later cannot touch the hosts file.
/// </summary>
public static class ElevationHelper
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
