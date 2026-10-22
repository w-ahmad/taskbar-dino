using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Platform;

/// <summary>
/// Reports foreground-window changes, event-driven (no polling). The app debounces these before
/// moving the dino so rapid Alt+Tabbing doesn't make it frantic.
/// </summary>
public interface IActiveWindowTracker : IDisposable
{
    /// <summary>The current foreground window, or <see langword="null"/> if none/desktop.</summary>
    ActiveWindowInfo? Current { get; }

    /// <summary>Raised when the foreground window changes. Fires on the UI thread.</summary>
    event EventHandler<ActiveWindowInfo?>? ForegroundChanged;

    /// <summary>Installs the hook and captures the initial foreground window.</summary>
    void Start();
}

/// <summary>Enumerates displays and resolves which monitor a window or point is on, with per-monitor DPI.</summary>
public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> GetMonitors();

    MonitorInfo Primary { get; }

    /// <summary>The bounding rectangle that encloses all monitors (the virtual desktop).</summary>
    PixelRect VirtualBounds { get; }

    MonitorInfo FromWindow(nint windowHandle);

    MonitorInfo FromPoint(PixelPoint point);
}

/// <summary>Resolves the taskbar (edge + bounds) for a given monitor so the dino stays glued to it.</summary>
public interface ITaskbarTracker
{
    TaskbarInfo GetTaskbar(MonitorInfo monitor);
}
