using System.Runtime.InteropServices;

using TaskbarDino.Core.Platform;
using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace TaskbarDino.Windows.Monitors;

/// <summary>Enumerates monitors and resolves the monitor for a window/point, with per-monitor DPI.</summary>
public sealed unsafe class MonitorService : IMonitorService
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        BOOL Callback(HMONITOR handle, HDC hdc, RECT* clip, LPARAM data)
        {
            monitors.Add(Build(handle));
            return true;
        }

        _ = PInvoke.EnumDisplayMonitors(HDC.Null, (RECT*)null, Callback, default);
        return monitors.Count > 0 ? monitors : [Primary];
    }

    public MonitorInfo Primary => Build(PInvoke.MonitorFromPoint(new System.Drawing.Point(0, 0), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY));

    public PixelRect VirtualBounds
    {
        get
        {
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            foreach (MonitorInfo monitor in GetMonitors())
            {
                left = Math.Min(left, monitor.Bounds.Left);
                top = Math.Min(top, monitor.Bounds.Top);
                right = Math.Max(right, monitor.Bounds.Right);
                bottom = Math.Max(bottom, monitor.Bounds.Bottom);
            }

            return PixelRect.FromLTRB(left, top, right, bottom);
        }
    }

    public MonitorInfo FromWindow(nint windowHandle) =>
        Build(PInvoke.MonitorFromWindow((HWND)windowHandle, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));

    public MonitorInfo FromPoint(PixelPoint point) =>
        Build(PInvoke.MonitorFromPoint(new System.Drawing.Point(point.X, point.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));

    internal static MonitorInfo Build(HMONITOR handle)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        _ = PInvoke.GetMonitorInfo(handle, ref info);

        PixelRect bounds = PixelRect.FromLTRB(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
        PixelRect work = PixelRect.FromLTRB(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
        bool isPrimary = (info.dwFlags & 1u) != 0; // MONITORINFOF_PRIMARY

        uint dpi = 96;
        if (PInvoke.GetDpiForMonitor(handle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out _).Succeeded)
        {
            dpi = dpiX;
        }

        return new MonitorInfo(handle, string.Empty, bounds, work, dpi, isPrimary);
    }
}
