using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MYTC.App.Windows;

public readonly record struct WindowPlacementBounds(
    double Left,
    double Top,
    double Width,
    double Height,
    double MinimumWidth,
    double MinimumHeight);

public static class AdaptiveWindowPlacement
{
    private const uint MonitorDefaultToNearest = 2;

    public static void FitInitialWindowToWorkingArea(
        Window window,
        double desiredWidth,
        double desiredHeight,
        double requestedMinimumWidth,
        double requestedMinimumHeight)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        var workingArea = TryGetWorkingArea(handle) ??
            SystemParameters.WorkArea;
        var placement = Calculate(
            workingArea,
            desiredWidth,
            desiredHeight,
            requestedMinimumWidth,
            requestedMinimumHeight);

        window.MinWidth = placement.MinimumWidth;
        window.MinHeight = placement.MinimumHeight;
        window.Width = placement.Width;
        window.Height = placement.Height;
        window.Left = placement.Left;
        window.Top = placement.Top;
    }

    public static WindowPlacementBounds Calculate(
        Rect workingArea,
        double desiredWidth,
        double desiredHeight,
        double requestedMinimumWidth,
        double requestedMinimumHeight,
        double outerMargin = 8)
    {
        if (workingArea.Width <= 0 ||
            workingArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workingArea),
                "屏幕工作区必须大于零。");
        }

        var horizontalMargin = Math.Min(
            Math.Max(outerMargin, 0),
            workingArea.Width / 4);
        var verticalMargin = Math.Min(
            Math.Max(outerMargin, 0),
            workingArea.Height / 4);
        var availableWidth = Math.Max(
            1,
            workingArea.Width - horizontalMargin * 2);
        var availableHeight = Math.Max(
            1,
            workingArea.Height - verticalMargin * 2);
        var minimumWidth = Math.Min(
            requestedMinimumWidth,
            availableWidth);
        var minimumHeight = Math.Min(
            requestedMinimumHeight,
            availableHeight);
        var width = Math.Clamp(
            desiredWidth,
            minimumWidth,
            availableWidth);
        var height = Math.Clamp(
            desiredHeight,
            minimumHeight,
            availableHeight);

        return new WindowPlacementBounds(
            workingArea.Left +
                (workingArea.Width - width) / 2,
            workingArea.Top +
                (workingArea.Height - height) / 2,
            width,
            height,
            minimumWidth,
            minimumHeight);
    }

    private static Rect? TryGetWorkingArea(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return null;
        }

        var monitor = MonitorFromWindow(
            window,
            MonitorDefaultToNearest);
        var info = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
        };
        if (monitor == IntPtr.Zero ||
            !GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var dpi = GetDpiForWindow(window);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        return new Rect(
            info.WorkArea.Left / scale,
            info.WorkArea.Top / scale,
            (info.WorkArea.Right - info.WorkArea.Left) / scale,
            (info.WorkArea.Bottom - info.WorkArea.Top) / scale);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(
        IntPtr window,
        uint flags);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
