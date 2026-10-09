using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EntityTracker.Screenshots;

/// <summary>
/// Keeps every main-window screenshot at 1920 × 1080, whatever screen and display scaling the tool
/// runs on. Windows normally limits a window to the size of the screen it is on, which with display
/// scaling is smaller than 1920 × 1080 in WPF's units; the screenshot window is off-screen, so it is
/// allowed to be larger.
/// </summary>
internal static class ScreenshotWindowSize
{
    internal const int Width = 1920;
    internal const int Height = 1080;

    private const int WmGetMinMaxInfo = 0x0024;

    internal static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(AllowLargerThanScreen);
        window.Width = Width;
        window.Height = Height;
    }

    /// <summary>Fails when a capture of the whole window is not exactly 1920 × 1080.</summary>
    internal static void EnsureFullSize(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (Math.Round(root.ActualWidth) != Width || Math.Round(root.ActualHeight) != Height)
        {
            throw new InvalidOperationException(
                $"The screenshot window is {root.ActualWidth:0} × {root.ActualHeight:0}, not {Width} × {Height}.");
        }
    }

    private static IntPtr AllowLargerThanScreen(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo) return IntPtr.Zero;
        MinMaxInfo info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        info.MaxTrackSize = new NativePoint(int.MaxValue / 2, int.MaxValue / 2);
        info.MaxSize = info.MaxTrackSize;
        Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }
}
