using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Platform;

/// <summary>Which screen edge the taskbar is docked to.</summary>
public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
/// A display, as reported by the Windows monitor APIs. Bounds and work area are in physical pixels;
/// <see cref="Dpi"/> is the monitor's effective DPI (96 = 100% scaling).
/// </summary>
public sealed record MonitorInfo(
    nint Handle,
    string DeviceName,
    PixelRect Bounds,
    PixelRect WorkArea,
    uint Dpi,
    bool IsPrimary)
{
    /// <summary>DPI scale factor (1.0 at 96 DPI / 100%, 1.5 at 144 DPI / 150%, …).</summary>
    public float DpiScale => Dpi / 96f;
}

/// <summary>
/// A taskbar instance (there is one per monitor on multi-monitor setups). Used to compute the Home
/// Position and to keep the dino glued to the taskbar as it relocates or auto-hides.
/// </summary>
public sealed record TaskbarInfo(
    PixelRect Bounds,
    TaskbarEdge Edge,
    bool AutoHide,
    MonitorInfo Monitor)
{
    /// <summary>Whether a taskbar is actually shown on this monitor (false on displays with none).</summary>
    public bool IsPresent { get; init; } = true;
}
