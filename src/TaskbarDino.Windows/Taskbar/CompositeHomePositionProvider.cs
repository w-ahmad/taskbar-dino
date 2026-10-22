using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Platform;
using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Windows.Taskbar;

/// <summary>
/// The Home Position strategy: try the precise UI-Automation taskbar-button locator first (so the
/// dino sits right on the active app's button), and fall back to the best-effort window-projection
/// provider when UIA can't find a match. This is the "multiple implementations, graceful degradation"
/// design — the rest of the app just sees <see cref="IHomePositionProvider"/>.
/// </summary>
public sealed class CompositeHomePositionProvider(
    UiaTaskbarButtonLocator locator,
    ActiveWindowHomePositionProvider fallback,
    ITaskbarTracker taskbars,
    IMonitorService monitors,
    ILogger<CompositeHomePositionProvider> logger) : IHomePositionProvider
{
    private readonly UiaTaskbarButtonLocator _locator = locator;
    private readonly ActiveWindowHomePositionProvider _fallback = fallback;
    private readonly ITaskbarTracker _taskbars = taskbars;
    private readonly IMonitorService _monitors = monitors;
    private readonly ILogger<CompositeHomePositionProvider> _logger = logger;

    public HomePosition GetHomePosition(ActiveWindowInfo? activeWindow)
    {
        if (activeWindow is not null && _locator.TryGetButtonRect(activeWindow, out PixelRect button))
        {
            _logger.LogDebug("Precise home over '{App}' button.", activeWindow.ProcessName);
            return AnchorOnTaskbar(button, HomeConfidence.Precise);
        }

        // No active app (desktop / Win+D) OR UIA couldn't find the button (unknown app, Terminal,
        // same-name collision, etc.) → the Start button is the default home.
        string reason = activeWindow is null ? "desktop / no active app" : $"button not found for '{activeWindow.ProcessName}'";
        if (_locator.TryGetStartButtonRect(out PixelRect start))
        {
            _logger.LogDebug("Home at the Start button ({Reason}).", reason);
            return AnchorOnTaskbar(start, HomeConfidence.Fallback);
        }

        return _fallback.GetHomePosition(activeWindow);
    }

    /// <summary>Projects a taskbar element's centre onto the inner edge of its monitor's taskbar.</summary>
    private HomePosition AnchorOnTaskbar(PixelRect element, HomeConfidence confidence)
    {
        MonitorInfo monitor = _monitors.FromPoint(element.Center);
        TaskbarInfo taskbar = _taskbars.GetTaskbar(monitor);
        PixelPoint anchor = taskbar.Edge switch
        {
            TaskbarEdge.Top => new PixelPoint(element.Center.X, taskbar.Bounds.Bottom),
            TaskbarEdge.Left => new PixelPoint(taskbar.Bounds.Right, element.Center.Y),
            TaskbarEdge.Right => new PixelPoint(taskbar.Bounds.Left, element.Center.Y),
            _ => new PixelPoint(element.Center.X, taskbar.Bounds.Top),
        };

        return new HomePosition(anchor, taskbar.Edge, monitor, confidence, element);
    }
}
