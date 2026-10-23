using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace TaskbarDino.Win32;

/// <summary>A display: bounds and work area in physical pixels, effective DPI (96 = 100%).</summary>
internal sealed record MonitorInfo(nint Handle, PixelRect Bounds, PixelRect WorkArea, uint Dpi)
{
    public float DpiScale => Dpi / 96f;
}

/// <summary>Which screen edge the taskbar is docked to.</summary>
internal enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
/// A monitor's taskbar, derived from the inset between its bounds and work area (robust across
/// Windows versions and multi-monitor layouts). <see cref="IsPresent"/> is false on a display with
/// no taskbar at all; <see cref="AutoHide"/> when a taskbar window exists but reserves no space.
/// </summary>
internal sealed record TaskbarInfo(PixelRect Bounds, TaskbarEdge Edge, bool AutoHide, bool IsPresent)
{
    /// <summary>The Y the dino's feet stand on for this monitor: the taskbar's top edge, or the screen bottom.</summary>
    public int GroundY(MonitorInfo monitor) => IsPresent && !AutoHide && Edge == TaskbarEdge.Bottom ? Bounds.Top
        : IsPresent && !AutoHide ? monitor.WorkArea.Bottom
        : monitor.Bounds.Bottom;
}

/// <summary>Monitor enumeration and taskbar layout queries (all cheap, uncached Win32 calls).</summary>
internal static unsafe class Monitors
{
    [ThreadStatic]
    private static List<MonitorInfo>? _enumScratch;

    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>(2);
        _enumScratch = list;
        _ = PInvoke.EnumDisplayMonitors(HDC.Null, (RECT*)null, &EnumProc, default);
        _enumScratch = null;
        if (list.Count == 0)
        {
            list.Add(Primary);
        }

        return list;
    }

    public static MonitorInfo Primary => Build(PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY));

    public static MonitorInfo FromWindow(HWND window) => Build(PInvoke.MonitorFromWindow(window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));

    public static MonitorInfo FromPoint(PixelPoint p) =>
        Build(PInvoke.MonitorFromPoint(new System.Drawing.Point(p.X, p.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));

    /// <summary>Re-reads a monitor by handle; falls back to the nearest one if it was disconnected.</summary>
    public static MonitorInfo Refresh(MonitorInfo monitor)
    {
        foreach (MonitorInfo m in All())
        {
            if (m.Handle == monitor.Handle)
            {
                return m;
            }
        }

        return FromPoint(monitor.Bounds.Center);
    }

    public static TaskbarInfo Taskbar(MonitorInfo monitor)
    {
        PixelRect b = monitor.Bounds;
        PixelRect w = monitor.WorkArea;

        if (w.Bottom < b.Bottom) return new TaskbarInfo(PixelRect.FromLTRB(b.Left, w.Bottom, b.Right, b.Bottom), TaskbarEdge.Bottom, false, true);
        if (w.Top > b.Top) return new TaskbarInfo(PixelRect.FromLTRB(b.Left, b.Top, b.Right, w.Top), TaskbarEdge.Top, false, true);
        if (w.Left > b.Left) return new TaskbarInfo(PixelRect.FromLTRB(b.Left, b.Top, w.Left, b.Bottom), TaskbarEdge.Left, false, true);
        if (w.Right < b.Right) return new TaskbarInfo(PixelRect.FromLTRB(w.Right, b.Top, b.Right, b.Bottom), TaskbarEdge.Right, false, true);

        // No reserved space: an auto-hidden taskbar (a taskbar window is on this monitor) or none at all.
        bool present = TaskbarWindows.AnyOn(monitor);
        int thickness = (int)MathF.Round(48 * monitor.DpiScale);
        return new TaskbarInfo(PixelRect.FromLTRB(b.Left, b.Bottom - thickness, b.Right, b.Bottom), TaskbarEdge.Bottom, AutoHide: present, IsPresent: present);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL EnumProc(HMONITOR handle, HDC hdc, RECT* clip, LPARAM data)
    {
        _enumScratch?.Add(Build(handle));
        return true;
    }

    private static MonitorInfo Build(HMONITOR handle)
    {
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        _ = PInvoke.GetMonitorInfo(handle, &info);

        PixelRect bounds = PixelRect.FromLTRB(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
        PixelRect work = PixelRect.FromLTRB(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
        uint dpi = 96;
        uint dpiX, dpiY;
        if (PInvoke.GetDpiForMonitor(handle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, &dpiX, &dpiY).Succeeded)
        {
            dpi = dpiX;
        }

        return new MonitorInfo(handle, bounds, work, dpi);
    }
}

/// <summary>Finds the shell's taskbar windows (<c>Shell_TrayWnd</c> + every <c>Shell_SecondaryTrayWnd</c>).</summary>
internal static unsafe class TaskbarWindows
{
    [ThreadStatic]
    private static List<HWND>? _scratch;

    [ThreadStatic]
    private static List<HWND>? _reusable;

    /// <summary>The taskbar windows, in a list reused between calls (valid until the next call).</summary>
    public static List<HWND> All()
    {
        List<HWND> list = _reusable ??= new List<HWND>(2);
        list.Clear();
        _scratch = list;
        _ = PInvoke.EnumWindows(&Collect, default);
        _scratch = null;
        return list;
    }

    public static bool AnyOn(MonitorInfo monitor)
    {
        foreach (HWND hwnd in All())
        {
            if (PInvoke.IsWindowVisible(hwnd) && (nint)PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST).Value == monitor.Handle)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsTaskbarClass(ReadOnlySpan<char> cls) =>
        cls.SequenceEqual("Shell_TrayWnd") || cls.SequenceEqual("Shell_SecondaryTrayWnd");

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL Collect(HWND hwnd, LPARAM _)
    {
        Span<char> buffer = stackalloc char[64];
        int length = WindowText.ClassName(hwnd, buffer);
        if (IsTaskbarClass(buffer[..length]))
        {
            _scratch?.Add(hwnd);
        }

        return true;
    }
}

/// <summary>Stack-buffer readers for window class names and titles.</summary>
internal static unsafe class WindowText
{
    public static int ClassName(HWND hwnd, Span<char> buffer)
    {
        fixed (char* p = buffer)
        {
            return Math.Max(0, PInvoke.GetClassName(hwnd, p, buffer.Length));
        }
    }

    public static string Title(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        int length;
        fixed (char* p = buffer)
        {
            length = PInvoke.GetWindowText(hwnd, p, buffer.Length);
        }

        return length > 0 ? new string(buffer[..length]) : string.Empty;
    }
}
