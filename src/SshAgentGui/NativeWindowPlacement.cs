using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SshAgentGui;

internal static class NativeWindowPlacement
{
    private const int SwShownormal = 1;
    private const int SwShowmaximized = 3;
    private const int SmXvirtualscreen = 76;
    private const int SmYvirtualscreen = 77;
    private const int SmCxvirtualscreen = 78;
    private const int SmCyvirtualscreen = 79;

    public static bool TryCapture(Window window, out RectPixels bounds, out bool maximized)
    {
        bounds = default;
        maximized = false;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return false;

        var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref placement))
            return false;

        bounds = RectPixels.FromNative(placement.NormalPosition);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        maximized = placement.ShowCmd == SwShowmaximized;
        return true;
    }

    public static bool TryApply(Window window, RectPixels bounds, bool maximized)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.IntersectsVirtualScreen())
            return false;

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var placement = new WindowPlacement
        {
            Length = Marshal.SizeOf<WindowPlacement>(),
            ShowCmd = maximized ? SwShowmaximized : SwShownormal,
            NormalPosition = bounds.ToNative(),
        };
        return SetWindowPlacement(hwnd, ref placement);
    }

    internal readonly record struct RectPixels(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public static RectPixels FromNative(RectNative rect) =>
            new(rect.Left, rect.Top, rect.Right, rect.Bottom);

        public RectNative ToNative() => new()
        {
            Left = Left,
            Top = Top,
            Right = Right,
            Bottom = Bottom,
        };

        public bool IntersectsVirtualScreen()
        {
            var screen = new RectPixels(
                GetSystemMetrics(SmXvirtualscreen),
                GetSystemMetrics(SmYvirtualscreen),
                GetSystemMetrics(SmXvirtualscreen) + GetSystemMetrics(SmCxvirtualscreen),
                GetSystemMetrics(SmYvirtualscreen) + GetSystemMetrics(SmCyvirtualscreen));
            return Left < screen.Right && Right > screen.Left && Top < screen.Bottom && Bottom > screen.Top;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public PointNative MinPosition;
        public PointNative MaxPosition;
        public RectNative NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
