namespace ToolManager;

using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

internal static class PopupPlacement
{
    private delegate bool EnumWindow(nint window, nint parameter);

    public static PixelPoint Cursor()
    {
        Native.Check(GetCursorPos(out var point));
        return new(point.X, point.Y);
    }

    public static TaskbarEdge Edge(PixelRect monitor, PixelRect bar)
    {
        if (bar.Width >= bar.Height)
        {
            return Math.Abs(bar.Top - monitor.Top) <= Math.Abs(monitor.Bottom - bar.Bottom) ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        }

        return Math.Abs(bar.Left - monitor.Left) <= Math.Abs(monitor.Right - bar.Right) ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    public static PopupLocation Detect(PixelPoint anchor)
    {
        var point = new Point
        {
            X = anchor.X,
            Y = anchor.Y,
        };
        var monitor = MonitorFromPoint(point, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Native.Check(GetMonitorInfo(monitor, ref info));
        var bounds = info.Monitor.Pixels;
        var bars = new List<PixelRect>();
        void Add(nint window, bool known)
        {
            if (window == nint.Zero || !IsWindowVisible(window) || !GetWindowRect(window, out var rect))
            {
                return;
            }

            var r = rect.Pixels;
            if (r.Width <= 0 || r.Height <= 0 || r.Right <= bounds.Left || r.Left >= bounds.Right || r.Bottom <= bounds.Top || r.Top >= bounds.Bottom)
            {
                return;
            }

            // A custom taskbar must be a thin dock at an edge, not an ordinary app window under the pointer.
            var horizontal = r.Width >= r.Height;
            bool thin = horizontal ? r.Height <= bounds.Height / 4 && r.Width >= bounds.Width / 4 : r.Width <= bounds.Width / 4 && r.Height >= bounds.Height / 4;
            bool atEdge = horizontal ? Math.Min(Math.Abs(r.Top - bounds.Top), Math.Abs(bounds.Bottom - r.Bottom)) <= 16 : Math.Min(Math.Abs(r.Left - bounds.Left), Math.Abs(bounds.Right - r.Right)) <= 16;
            if (thin && (known || atEdge))
            {
                bars.Add(r);
            }
        }

        var clicked = GetAncestor(WindowFromPoint(point), 2);
        Add(clicked, false);
        EnumWindows(
            (window, _) =>
            {
                var name = new StringBuilder(256);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                {
                    Add(window, true);
                }

                return true;
            },
            nint.Zero);
        PixelRect? bar = bars.OrderBy(r => Distance(r, anchor)).Select(r => (PixelRect?)r).FirstOrDefault();
        TaskbarEdge edge;
        if (bar is PixelRect actual)
        {
            edge = Edge(bounds, actual);
        }
        else
        {
            // If a shell replacement exposes no taskbar window, use the clicked edge and its reported work-area inset.
            var distances = new[] { (TaskbarEdge.Left, Math.Abs(anchor.X - bounds.Left)), (TaskbarEdge.Top, Math.Abs(anchor.Y - bounds.Top)), (TaskbarEdge.Right, Math.Abs(bounds.Right - anchor.X)), (TaskbarEdge.Bottom, Math.Abs(bounds.Bottom - anchor.Y)) };
            edge = distances.OrderBy(v => v.Item2).First().Item1;
        }

        uint dpi = 96;
        if (GetDpiForMonitor(monitor, 0, out var x, out _) == 0)
        {
            dpi = x;
        }

        return new(bounds, info.Work.Pixels, bar, edge, anchor, dpi);
    }

    public static PixelRect Calculate(PopupLocation location, int width, int height, int gap)
    {
        var work = location.Work;
        if (location.Taskbar is PixelRect bar)
        {
            work = location.Edge switch
            {
                TaskbarEdge.Left => work with { Left = Math.Max(work.Left, bar.Right) },
                TaskbarEdge.Top => work with { Top = Math.Max(work.Top, bar.Bottom) },
                TaskbarEdge.Right => work with { Right = Math.Min(work.Right, bar.Left) },
                _ => work with { Bottom = Math.Min(work.Bottom, bar.Top) },
            };
        }

        gap = Math.Max(0, Math.Min(gap, Math.Min(work.Width, work.Height) / 4));
        var left = work.Left + gap;
        var top = work.Top + gap;
        var right = work.Right - gap;
        var bottom = work.Bottom - gap;
        width = Math.Min(width, Math.Max(1, right - left));
        height = Math.Min(height, Math.Max(1, bottom - top));
        int x = location.Anchor.X - (width / 2), y = location.Anchor.Y - (height / 2);
        switch (location.Edge)
        {
            case TaskbarEdge.Left:
                x = left;
                break;
            case TaskbarEdge.Top:
                y = top;
                break;
            case TaskbarEdge.Right:
                x = right - width;
                break;
            case TaskbarEdge.Bottom:
                y = bottom - height;
                break;
        }

        x = Math.Clamp(x, left, Math.Max(left, right - width));
        y = Math.Clamp(y, top, Math.Max(top, bottom - height));
        return new(x, y, x + width, y + height);
    }

    public static PixelRect Position(Window window, PixelPoint anchor)
    {
        var location = Detect(anchor);
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var scale = location.Dpi / 96d;
        var rect = Calculate(location, (int)Math.Ceiling(window.Width * scale), (int)Math.Ceiling(window.Height * scale), (int)Math.Ceiling(8 * scale));
        Native.Check(SetWindowPos(handle, nint.Zero, rect.Left, rect.Top, rect.Width, rect.Height, 0x14));

        // WPF can resize on WM_DPICHANGED. Re-anchor the resulting physical window bounds on the target monitor.
        if (GetWindowRect(handle, out var actual))
        {
            rect = Calculate(location, actual.Pixels.Width, actual.Pixels.Height, (int)Math.Ceiling(8 * scale));
            Native.Check(SetWindowPos(handle, nint.Zero, rect.Left, rect.Top, rect.Width, rect.Height, 0x14));
        }

        Native.Check(GetWindowRect(handle, out var final));
        return final.Pixels;
    }

    [DllImport("user32")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32")]
    private static extern bool EnumWindows(EnumWindow callback, nint parameter);

    [DllImport("user32", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int count);

    [DllImport("user32")]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("shcore")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);

    [DllImport("user32", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    private static long Distance(PixelRect r, PixelPoint p)
    {
        long x = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right), y = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
        return (x * x) + (y * y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public PixelRect Pixels => new(this.Left, this.Top, this.Right, this.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}
