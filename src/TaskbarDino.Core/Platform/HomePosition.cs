using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Platform;

/// <summary>How the Home Position was determined — surfaced so the app can log/diagnose accuracy.</summary>
public enum HomeConfidence
{
    /// <summary>Located the exact taskbar button (via UI Automation).</summary>
    Precise,

    /// <summary>Placed relative to the taskbar using a best-effort anchor.</summary>
    BestEffort,

    /// <summary>A safe fallback (e.g. taskbar could not be found); dino still has somewhere to live.</summary>
    Fallback,
}

/// <summary>
/// Where the dino should call home — the single abstraction that decouples game/companion logic
/// from Windows taskbar internals. <see cref="Anchor"/> is the point the dino's feet rest on, in
/// physical screen pixels; the dino stands "on top of" the taskbar there.
/// </summary>
public sealed record HomePosition(
    PixelPoint Anchor,
    TaskbarEdge Edge,
    MonitorInfo Monitor,
    HomeConfidence Confidence,
    PixelRect? ButtonBounds = null)
{
    public float DpiScale => Monitor.DpiScale;
}

/// <summary>
/// Resolves the Home Position for the current foreground app. Multiple implementations exist behind
/// this interface (precise via UI Automation, best-effort via a taskbar anchor), composed so the app
/// degrades gracefully rather than failing when a precise position is unavailable.
/// </summary>
public interface IHomePositionProvider
{
    /// <summary>
    /// Computes where the dino should live given the active window (which may be <see langword="null"/>
    /// when there is no meaningful foreground app, e.g. the desktop).
    /// </summary>
    HomePosition GetHomePosition(ActiveWindowInfo? activeWindow);
}
