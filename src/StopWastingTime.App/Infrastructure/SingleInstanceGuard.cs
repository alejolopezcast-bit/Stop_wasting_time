namespace StopWastingTime.App.Infrastructure;

/// <summary>
/// Keeps a second copy of the app from starting. Two instances would fight over the same hosts file and
/// double every blocked distraction, so the launcher refuses instead of letting that happen.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Global\StopWastingTime.SingleInstance";

    private Mutex? _mutex;

    /// <summary>True when this process is the one that owns the app.</summary>
    public bool IsPrimaryInstance { get; private set; }

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

        if (!createdNew)
        {
            // Someone else owns it: release our handle and report failure.
            _mutex.Dispose();
            _mutex = null;
            IsPrimaryInstance = false;
            return false;
        }

        IsPrimaryInstance = true;
        return true;
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Already released, or never owned. Nothing to do.
            }
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
