using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Platform;

/// <summary>
/// Normalized pointer input for the dino, decoupled from Win32 message plumbing. All positions are
/// in physical screen pixels. Backed by a global low-level mouse hook so the overlay window can stay
/// fully click-through (never blocking the desktop) while the dino still reacts to the pointer.
/// Consumed by the companion runtime to drive reactions (click), gaze (hover), pick-up (drag) and
/// the game (double-click).
/// </summary>
public interface IInputSource : IDisposable
{
    /// <summary>Installs the global hook. Call once at startup, on the UI thread.</summary>
    void Start();

    /// <summary>
    /// Supplies the "is this screen point on the dino?" test. Events fire and pointer input over the
    /// dino is captured only when this returns <see langword="true"/>; everything else passes through.
    /// </summary>
    void SetHitTest(Func<PixelPoint, bool>? hitTest);

    /// <summary>The pointer moved over the dino (not while dragging).</summary>
    event EventHandler<PixelPoint>? PointerHover;

    /// <summary>The pointer left the dino.</summary>
    event EventHandler? PointerLeft;

    /// <summary>A single left-click landed on the dino.</summary>
    event EventHandler<PixelPoint>? Clicked;

    /// <summary>A left double-click landed on the dino.</summary>
    event EventHandler<PixelPoint>? DoubleClicked;

    /// <summary>A right-click landed on the dino (used to raise the context menu).</summary>
    event EventHandler<PixelPoint>? RightClicked;

    /// <summary>The dino began being dragged.</summary>
    event EventHandler<PixelPoint>? DragStarted;

    /// <summary>The dino was dragged to a new position.</summary>
    event EventHandler<PixelPoint>? DragMoved;

    /// <summary>The dino was released after a drag.</summary>
    event EventHandler<PixelPoint>? DragReleased;
}
