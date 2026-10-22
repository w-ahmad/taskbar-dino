using TaskbarDino.Core.Platform;
using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace TaskbarDino.Windows.Taskbar;

/// <summary>
/// Derives each monitor's taskbar from the inset between its full bounds and its work area — a
/// robust, multi-monitor, version-independent approach that needs no fragile taskbar-window probing.
/// When there is no inset it distinguishes an auto-hidden taskbar (a taskbar window IS on the monitor)
/// from a monitor with NO taskbar at all (none is), so the dino can stand on the screen's bottom edge
/// rather than floating at a phantom taskbar height.
/// </summary>
public sealed unsafe class WorkAreaTaskbarTracker : ITaskbarTracker
{
    private const int DefaultThickness96 = 48;

    public TaskbarInfo GetTaskbar(MonitorInfo monitor)
    {
        PixelRect b = monitor.Bounds;
        PixelRect w = monitor.WorkArea;

        if (w.Bottom < b.Bottom)
        {
            return new TaskbarInfo(PixelRect.FromLTRB(b.Left, w.Bottom, b.Right, b.Bottom), TaskbarEdge.Bottom, AutoHide: false, monitor);
        }

        if (w.Top > b.Top)
        {
            return new TaskbarInfo(PixelRect.FromLTRB(b.Left, b.Top, b.Right, w.Top), TaskbarEdge.Top, AutoHide: false, monitor);
        }

        if (w.Left > b.Left)
        {
            return new TaskbarInfo(PixelRect.FromLTRB(b.Left, b.Top, w.Left, b.Bottom), TaskbarEdge.Left, AutoHide: false, monitor);
        }

        if (w.Right < b.Right)
        {
            return new TaskbarInfo(PixelRect.FromLTRB(w.Right, b.Top, b.Right, b.Bottom), TaskbarEdge.Right, AutoHide: false, monitor);
        }

        // No reserved space: either an auto-hidden taskbar (a taskbar window is present on this
        // monitor) or a monitor with no taskbar at all.
        bool present = HasTaskbarWindow(monitor);
        int thickness = (int)MathF.Round(DefaultThickness96 * monitor.DpiScale);
        return new TaskbarInfo(
            PixelRect.FromLTRB(b.Left, b.Bottom - thickness, b.Right, b.Bottom),
            TaskbarEdge.Bottom,
            AutoHide: present,
            monitor)
        {
            IsPresent = present,
        };
    }

    private static bool HasTaskbarWindow(MonitorInfo monitor)
    {
        bool found = false;

        BOOL Check(HWND hwnd, LPARAM _)
        {
            if (!PInvoke.IsWindowVisible(hwnd))
            {
                return true;
            }

            string cls = ReadClassName(hwnd);
            if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                HMONITOR mon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
                if ((nint)mon == monitor.Handle)
                {
                    found = true;
                    return false; // stop enumerating
                }
            }

            return true;
        }

        _ = PInvoke.EnumWindows(Check, default);
        return found;
    }

    private static string ReadClassName(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[128];
        int length;
        fixed (char* p = buffer)
        {
            length = PInvoke.GetClassName(hwnd, new PWSTR(p), buffer.Length);
        }

        return length > 0 ? new string(buffer[..length]) : string.Empty;
    }
}
