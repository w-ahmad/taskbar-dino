using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Platform;
using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;

namespace TaskbarDino.Windows.Taskbar;

/// <summary>
/// Best-effort Home Position provider: the dino follows the foreground window by projecting that
/// window's centre onto the taskbar of the monitor it lives on. This is robust across Windows
/// versions and multi-monitor setups, and on Windows 11 (centred taskbar) it reads more naturally
/// than hunting the exact taskbar button. A precise UI-Automation button locator can slot in behind
/// this same interface later; this provider self-falls-back to the primary taskbar centre.
/// </summary>
public sealed unsafe class ActiveWindowHomePositionProvider(
    IMonitorService monitors,
    ITaskbarTracker taskbars,
    ILogger<ActiveWindowHomePositionProvider> logger) : IHomePositionProvider
{
    private const int EdgeMargin = 36;

    private readonly IMonitorService _monitors = monitors;
    private readonly ITaskbarTracker _taskbars = taskbars;
    private readonly ILogger<ActiveWindowHomePositionProvider> _logger = logger;

    public HomePosition GetHomePosition(ActiveWindowInfo? activeWindow)
    {
        MonitorInfo monitor;
        PixelPoint windowCenter;

        if (activeWindow is { Handle: var handle } && handle != 0 && TryGetWindowRect(handle, out PixelRect windowRect))
        {
            monitor = _monitors.FromWindow(handle);
            windowCenter = windowRect.Center;
        }
        else
        {
            monitor = _monitors.Primary;
            windowCenter = monitor.Bounds.Center;
        }

        TaskbarInfo taskbar = _taskbars.GetTaskbar(monitor);
        PixelPoint anchor = ProjectOntoTaskbar(taskbar, windowCenter);
        return new HomePosition(anchor, taskbar.Edge, monitor, HomeConfidence.BestEffort);
    }

    /// <summary>Places the feet on the taskbar's inner edge, aligned with the window along the bar.</summary>
    private static PixelPoint ProjectOntoTaskbar(TaskbarInfo taskbar, PixelPoint windowCenter)
    {
        PixelRect bar = taskbar.Bounds;
        return taskbar.Edge switch
        {
            TaskbarEdge.Top => new PixelPoint(ClampInclusive(windowCenter.X, bar.Left, bar.Right), bar.Bottom),
            TaskbarEdge.Left => new PixelPoint(bar.Right, ClampInclusive(windowCenter.Y, bar.Top, bar.Bottom)),
            TaskbarEdge.Right => new PixelPoint(bar.Left, ClampInclusive(windowCenter.Y, bar.Top, bar.Bottom)),
            _ => new PixelPoint(ClampInclusive(windowCenter.X, bar.Left, bar.Right), bar.Top),
        };
    }

    private static int ClampInclusive(int value, int min, int max) =>
        Math.Clamp(value, min + EdgeMargin, Math.Max(min + EdgeMargin, max - EdgeMargin));

    private static bool TryGetWindowRect(nint handle, out PixelRect rect)
    {
        if (PInvoke.GetWindowRect((HWND)handle, out RECT r))
        {
            rect = PixelRect.FromLTRB(r.left, r.top, r.right, r.bottom);
            return !rect.IsEmpty;
        }

        rect = default;
        return false;
    }
}
