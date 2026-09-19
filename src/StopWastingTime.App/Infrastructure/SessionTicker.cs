using System.Windows.Threading;
using StopWastingTime.Core.Sessions;

namespace StopWastingTime.App.Infrastructure;

/// <summary>
/// Drives the countdown for whichever screen started the session. One timer for the whole app: if each
/// screen kept its own, a session started on one of them would never advance while another was on show,
/// and two timers would tick the same session twice.
/// </summary>
public sealed class SessionTicker : IDisposable
{
    private readonly DispatcherTimer _timer;

    public SessionTicker(FocusSessionService sessions)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _timer.Tick += async (_, _) => await sessions.TickAsync();

        sessions.SessionStarted += (_, _) => _timer.Start();
        sessions.SessionEnded += (_, _) => _timer.Stop();
    }

    public void Dispose() => _timer.Stop();
}
