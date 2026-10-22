using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;

namespace TaskbarDino.Win32;

/// <summary>
/// Where the dino lives: <see cref="Anchor"/> is the point its feet rest on, in physical screen
/// pixels, on <see cref="Monitor"/>'s taskbar. <see cref="Precise"/> says whether the exact taskbar
/// button was found (false = Start button or best-effort projection).
/// </summary>
internal sealed record HomePosition(PixelPoint Anchor, MonitorInfo Monitor, bool Precise);

/// <summary>
/// The home-position strategy, in order: the active app's exact taskbar button (UI Automation);
/// the Start button when there is no active app or its button can't be identified; and finally the
/// active window's centre projected onto its monitor's taskbar, which needs no shell internals at all.
/// </summary>
internal sealed unsafe class HomeResolver(TaskbarButtonLocator locator)
{
    private const int EdgeMargin = 36;

    private readonly TaskbarButtonLocator _locator = locator;

    public HomePosition Resolve(ActiveWindow? active)
    {
        if (active is not null && _locator.TryGetButtonRect(active, out PixelRect button))
        {
            return AnchorOn(button, precise: true);
        }

        if (_locator.TryGetStartButtonRect(out PixelRect start))
        {
            return AnchorOn(start, precise: false);
        }

        return Fallback(active);
    }

    /// <summary>Projects a taskbar element's centre onto the inner edge of its monitor's taskbar.</summary>
    private static HomePosition AnchorOn(PixelRect element, bool precise)
    {
        MonitorInfo monitor = Monitors.FromPoint(element.Center);
        TaskbarInfo taskbar = Monitors.Taskbar(monitor);
        int x = Math.Clamp(element.Center.X, monitor.Bounds.Left + EdgeMargin, monitor.Bounds.Right - EdgeMargin);
        return new HomePosition(new PixelPoint(x, taskbar.GroundY(monitor)), monitor, precise);
    }

    private static HomePosition Fallback(ActiveWindow? active)
    {
        MonitorInfo monitor;
        int centerX;
        RECT r;
        if (active is not null && PInvoke.IsWindow(active.Handle) && PInvoke.GetWindowRect(active.Handle, &r))
        {
            monitor = Monitors.FromWindow(active.Handle);
            centerX = (r.left + r.right) / 2;
        }
        else
        {
            monitor = Monitors.Primary;
            centerX = monitor.Bounds.Center.X;
        }

        TaskbarInfo taskbar = Monitors.Taskbar(monitor);
        int x = Math.Clamp(centerX, monitor.Bounds.Left + EdgeMargin, monitor.Bounds.Right - EdgeMargin);
        return new HomePosition(new PixelPoint(x, taskbar.GroundY(monitor)), monitor, Precise: false);
    }
}
