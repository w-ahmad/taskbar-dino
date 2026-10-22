using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Platform;

/// <summary>
/// The transparent, top-most, click-through overlay the dino is drawn into. Implemented in the
/// Windows layer as a layered, no-redirection-bitmap Win32 window that never activates and never
/// appears in Alt+Tab. The renderer binds its DirectComposition target to <see cref="Handle"/>.
/// </summary>
public interface IOverlayWindow : IDisposable
{
    /// <summary>The native window handle (HWND) the renderer composes into.</summary>
    nint Handle { get; }

    /// <summary>Current window rectangle in physical screen pixels.</summary>
    PixelRect Bounds { get; }

    /// <summary>Whether the window is currently shown.</summary>
    bool IsVisible { get; }

    /// <summary>The window's current DPI (96 = 100% scaling).</summary>
    uint Dpi { get; }

    /// <summary>Raised after the window moves to a monitor with a different DPI (from <c>WM_DPICHANGED</c>).</summary>
    event EventHandler<uint>? DpiChanged;

    /// <summary>Raised when the OS or a user affordance asks the window to close (so the app can shut down).</summary>
    event EventHandler? CloseRequested;

    /// <summary>Raised when the Windows accent / colorization colour changes (so the Accent theme can refresh).</summary>
    event EventHandler? AccentColorChanged;

    /// <summary>Moves/resizes the window without activating it.</summary>
    void SetBounds(PixelRect bounds);

    /// <summary>
    /// The overlay is always click-through (input passes through to the desktop); pointer interaction
    /// with the dino is handled out-of-band by <see cref="IInputSource"/>. This toggle exists only to
    /// force it opaque-to-input in special cases and is normally left enabled.
    /// </summary>
    void SetClickThrough(bool clickThrough);

    void Show();

    void Hide();
}
