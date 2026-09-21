using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StopWastingTime.App.Infrastructure;

/// <summary>
/// Keeps a window with a custom caption from covering the taskbar when it is maximised.
/// <para>
/// A window drawn with <see cref="System.Windows.Shell.WindowChrome"/> maximises to the whole monitor
/// rather than to the working area, which hides the taskbar and, when the taskbar auto hides, stops it
/// appearing at all. Windows asks for the size it should use through WM_GETMINMAXINFO, so that is where
/// the working area is handed back.
/// </para>
/// </summary>
public static class MaximizeBehaviour
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    public static void Apply(Window window)
    {
        if (window.IsLoaded)
        {
            Hook(window);
            return;
        }

        window.SourceInitialized += (_, _) => Hook(window);
    }

    private static void Hook(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return IntPtr.Zero;
        }

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);

        // Positions are relative to the monitor, so the working area is offset by the monitor origin.
        minMax.ptMaxPosition.x = info.rcWork.left - info.rcMonitor.left;
        minMax.ptMaxPosition.y = info.rcWork.top - info.rcMonitor.top;
        minMax.ptMaxSize.x = info.rcWork.right - info.rcWork.left;
        minMax.ptMaxSize.y = info.rcWork.bottom - info.rcWork.top;

        Marshal.StructureToPtr(minMax, lParam, fDeleteOld: true);
        handled = true;

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point ptReserved;
        public Point ptMaxSize;
        public Point ptMaxPosition;
        public Point ptMinTrackSize;
        public Point ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public int dwFlags;
    }
}
