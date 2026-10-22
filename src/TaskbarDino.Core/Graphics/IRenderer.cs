using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Graphics;

/// <summary>
/// The rendering seam. Everything above this interface is framework-agnostic; the production
/// implementation is DirectComposition + Direct2D (Vortice), but it could be swapped for
/// Windows.UI.Composition, Skia, etc. without touching game, animation or app logic.
/// <para>
/// The API is deliberately immediate-mode and batched (<see cref="BeginFrame"/> →
/// <see cref="DrawSprite"/>* → <see cref="EndFrame"/>) so it maps cleanly onto Direct2D's
/// <c>ID2D1SpriteBatch</c> and allocates nothing per frame.
/// </para>
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>Uploads a premultiplied BGRA pixel buffer as a GPU texture (e.g. a sprite atlas).</summary>
    TextureHandle CreateTexture(ReadOnlySpan<Bgra32> pixels, int width, int height);

    /// <summary>Replaces the contents of an existing texture in place (used when the theme changes).</summary>
    void UpdateTexture(TextureHandle texture, ReadOnlySpan<Bgra32> pixels, int width, int height);

    /// <summary>Resizes the render surface to a new physical pixel size and DPI scale (e.g. on DPI change).</summary>
    void Resize(int pixelWidth, int pixelHeight, float dpiScale);

    /// <summary>Begins a frame: binds the target and clears it to fully transparent.</summary>
    void BeginFrame();

    /// <summary>Draws one sprite region from a texture into a destination rectangle, optionally tinted/flipped.</summary>
    /// <param name="texture">The source texture / sprite atlas.</param>
    /// <param name="source">The atlas region (source rectangle + pivot) to draw.</param>
    /// <param name="destination">Destination rectangle in physical pixels.</param>
    /// <param name="tint">Colour multiplied over the sprite; use <see cref="Rgba.White"/> for no tint.</param>
    /// <param name="flipX">Mirror horizontally (used when the dino faces left).</param>
    void DrawSprite(TextureHandle texture, in AtlasRegion source, in RectF destination, Rgba tint, bool flipX = false);

    /// <summary>Draws a rectangle outline in physical pixels (used for debug affordances).</summary>
    void DrawRect(in RectF rect, Rgba color, float strokeWidth);

    /// <summary>Ends the frame and presents it to the DirectComposition visual tree.</summary>
    void EndFrame();
}
