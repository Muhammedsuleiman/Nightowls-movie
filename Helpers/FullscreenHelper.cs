using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NightOwls.Helpers;

public static class FullscreenHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    public static void Enter(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO();
        info.cbSize = Marshal.SizeOf<MONITORINFO>();
        if (!GetMonitorInfo(monitor, ref info)) return;

        window.WindowState = WindowState.Normal;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = true;
        window.Left = info.rcMonitor.Left;
        window.Top = info.rcMonitor.Top;
        window.Width = info.rcMonitor.Right - info.rcMonitor.Left;
        window.Height = info.rcMonitor.Bottom - info.rcMonitor.Top;
        window.Topmost = true;
    }

    public static Rect GetMonitorBounds(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return default;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO();
        info.cbSize = Marshal.SizeOf<MONITORINFO>();
        if (!GetMonitorInfo(monitor, ref info)) return default;
        return new Rect(info.rcMonitor.Left, info.rcMonitor.Top,
            info.rcMonitor.Right - info.rcMonitor.Left,
            info.rcMonitor.Bottom - info.rcMonitor.Top);
    }
}
