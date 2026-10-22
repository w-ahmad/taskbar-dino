using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Graphics;

namespace TaskbarDino.Rendering;

/// <summary>Creates <see cref="DirectCompositionRenderer"/> instances bound to a window.</summary>
public sealed class DirectCompositionRendererFactory(ILoggerFactory loggerFactory) : IRendererFactory
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public IRenderer Create(nint windowHandle, int pixelWidth, int pixelHeight, float dpiScale) =>
        new DirectCompositionRenderer(
            windowHandle,
            pixelWidth,
            pixelHeight,
            dpiScale,
            _loggerFactory.CreateLogger<DirectCompositionRenderer>());
}
