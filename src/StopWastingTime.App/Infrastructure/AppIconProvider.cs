using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StopWastingTime.App.Infrastructure;

/// <summary>
/// Reads the icon Windows shows for an executable, so the blocklist looks like the programs it is
/// talking about rather than a list of file names.
/// <para>
/// It goes through the shell API and hands the handle straight to WPF, which avoids taking a dependency
/// on System.Drawing for what amounts to two calls. Results are cached: the icon of an executable does
/// not change while the app is open.
/// </para>
/// </summary>
public sealed class AppIconProvider
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource? GetIcon(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        if (_cache.TryGetValue(executablePath, out var cached))
        {
            return cached;
        }

        var icon = Load(executablePath);
        _cache[executablePath] = icon;

        return icon;
    }

    private static ImageSource? Load(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            return null;
        }

        var info = default(ShFileInfo);
        var result = SHGetFileInfo(
            executablePath,
            fileAttributes: 0,
            ref info,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            ShellFlags.Icon | ShellFlags.LargeIcon);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // Frozen so it can be handed to the UI thread and cached without further copies.
            source.Freeze();

            return source;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    [Flags]
    private enum ShellFlags : uint
    {
        Icon = 0x000000100,
        LargeIcon = 0x000000000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShFileInfo info,
        uint sizeOfInfo,
        ShellFlags flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
