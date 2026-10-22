namespace TaskbarDino.Core.Graphics;

/// <summary>
/// Creates an <see cref="IRenderer"/> bound to a native window. Kept separate from
/// <see cref="IRenderer"/> because construction needs the window handle and initial size, which are
/// only known after the overlay window exists — this keeps the composition root free of that ordering.
/// </summary>
public interface IRendererFactory
{
    /// <summary>Creates a renderer that composes into <paramref name="windowHandle"/> (an HWND).</summary>
    IRenderer Create(nint windowHandle, int pixelWidth, int pixelHeight, float dpiScale);
}
