using System.Numerics;

using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;

using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

using DCommon = Vortice.DCommon;

namespace TaskbarDino.Rendering;

/// <summary>
/// GPU renderer for the transparent overlay. Pipeline:
/// <list type="bullet">
///   <item>a Direct3D 11 device (BGRA-capable) provides the GPU;</item>
///   <item>a DXGI <c>CreateSwapChainForComposition</c> swap chain (premultiplied B8G8R8A8) is the surface;</item>
///   <item>DirectComposition binds that swap chain to the HWND and the DWM composites it — giving true
///         per-pixel transparency on the GPU with no layered-window CPU blit;</item>
///   <item>a Direct2D device context draws sprites onto the swap-chain back buffer.</item>
/// </list>
/// Rendering is done in physical-pixel space (Direct2D DPI pinned to 96) — the app sizes sprites in
/// pixels using the monitor's scale factor, so pixel art stays crisp via nearest-neighbour sampling.
/// </summary>
public sealed class DirectCompositionRenderer : IRenderer
{
    private readonly ILogger _logger;
    private readonly List<ID2D1Bitmap> _textures = [];

    private ID3D11Device _d3dDevice = null!;
    private IDXGIDevice _dxgiDevice = null!;
    private IDXGISwapChain1 _swapChain = null!;
    private ID2D1Device _d2dDevice = null!;
    private ID2D1DeviceContext _d2dContext = null!;
    private ID2D1Bitmap1 _targetBitmap = null!;
    private ID2D1SolidColorBrush? _debugBrush;
    private IDCompositionDevice _dcompDevice = null!;
    private IDCompositionTarget _dcompTarget = null!;
    private IDCompositionVisual _dcompVisual = null!;

    private int _width;
    private int _height;

    public DirectCompositionRenderer(nint windowHandle, int pixelWidth, int pixelHeight, float dpiScale, ILogger logger)
    {
        _logger = logger;
        _width = Math.Max(1, pixelWidth);
        _height = Math.Max(1, pixelHeight);
        Initialize(windowHandle);
        _logger.LogInformation("DirectComposition renderer initialised ({Width}x{Height}, scale {Scale:0.##}).", _width, _height, dpiScale);
    }

    public TextureHandle CreateTexture(ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        var properties = new BitmapProperties1(
            new DCommon.PixelFormat(Format.B8G8R8A8_UNorm, DCommon.AlphaMode.Premultiplied),
            96f,
            96f,
            BitmapOptions.None);

        ID2D1Bitmap1 bitmap;
        unsafe
        {
            fixed (Bgra32* p = pixels)
            {
                bitmap = _d2dContext.CreateBitmap(new SizeI(width, height), (nint)p, (uint)(width * 4), properties);
            }
        }

        _textures.Add(bitmap);
        return new TextureHandle(_textures.Count); // 1-based; 0 == TextureHandle.None
    }

    public void UpdateTexture(TextureHandle texture, ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        if (!texture.IsValid || texture.Id > _textures.Count)
        {
            return;
        }

        var properties = new BitmapProperties1(
            new DCommon.PixelFormat(Format.B8G8R8A8_UNorm, DCommon.AlphaMode.Premultiplied),
            96f,
            96f,
            BitmapOptions.None);

        ID2D1Bitmap1 bitmap;
        unsafe
        {
            fixed (Bgra32* p = pixels)
            {
                bitmap = _d2dContext.CreateBitmap(new SizeI(width, height), (nint)p, (uint)(width * 4), properties);
            }
        }

        _textures[texture.Id - 1].Dispose();
        _textures[texture.Id - 1] = bitmap;
    }

    public void Resize(int pixelWidth, int pixelHeight, float dpiScale)
    {
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        if (pixelWidth == _width && pixelHeight == _height)
        {
            return;
        }

        _width = pixelWidth;
        _height = pixelHeight;

        _d2dContext.Target = null!;
        _targetBitmap.Dispose();
        _swapChain.ResizeBuffers(0, (uint)_width, (uint)_height, Format.Unknown, SwapChainFlags.None);
        CreateTargetBitmap();
    }

    public void BeginFrame()
    {
        _d2dContext.BeginDraw();
        _d2dContext.Clear(new Color4(0f, 0f, 0f, 0f));
    }

    public void DrawSprite(TextureHandle texture, in AtlasRegion source, in RectF destination, Rgba tint, bool flipX = false)
    {
        if (!texture.IsValid || texture.Id > _textures.Count)
        {
            return;
        }

        ID2D1Bitmap bitmap = _textures[texture.Id - 1];
        PixelRect src = source.Source;
        var sourceRect = new Rect(src.X, src.Y, src.Width, src.Height);
        var destRect = new Rect(destination.X, destination.Y, destination.Width, destination.Height);

        Matrix3x2 previous = _d2dContext.Transform;
        if (flipX)
        {
            // Mirror horizontally about the sprite's centre.
            _d2dContext.Transform = Matrix3x2.CreateScale(-1f, 1f, new Vector2(destination.Center.X, destination.Center.Y));
        }

        _d2dContext.DrawBitmap(bitmap, destRect, 1f, Vortice.Direct2D1.BitmapInterpolationMode.NearestNeighbor, sourceRect);

        if (flipX)
        {
            _d2dContext.Transform = previous;
        }
    }

    public void DrawRect(in RectF rect, Rgba color, float strokeWidth)
    {
        var c = new Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        _debugBrush ??= _d2dContext.CreateSolidColorBrush(c);
        _debugBrush.Color = c;
        _d2dContext.DrawRectangle(new Rect(rect.X, rect.Y, rect.Width, rect.Height), _debugBrush, strokeWidth);
    }

    public void EndFrame()
    {
        _d2dContext.EndDraw();
        _swapChain.Present(1, PresentFlags.None);
    }

    public void Dispose()
    {
        foreach (ID2D1Bitmap bitmap in _textures)
        {
            bitmap.Dispose();
        }

        _textures.Clear();

        _debugBrush?.Dispose();
        _targetBitmap?.Dispose();
        _dcompVisual?.Dispose();
        _dcompTarget?.Dispose();
        _dcompDevice?.Dispose();
        _d2dContext?.Dispose();
        _d2dDevice?.Dispose();
        _swapChain?.Dispose();
        _dxgiDevice?.Dispose();
        _d3dDevice?.Dispose();
    }

    private void Initialize(nint windowHandle)
    {
        DeviceCreationFlags creationFlags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        // creationFlags |= DeviceCreationFlags.Debug; // enable when the D3D11 debug layer is installed
#endif
        Vortice.Direct3D.FeatureLevel[] featureLevels =
        [
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1,
        ];

        try
        {
            _d3dDevice = D3D11.D3D11CreateDevice(DriverType.Hardware, creationFlags, featureLevels);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hardware Direct3D 11 device unavailable; falling back to the WARP software renderer.");
            _d3dDevice = D3D11.D3D11CreateDevice(DriverType.Warp, creationFlags, featureLevels);
        }

        _dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();

        // Direct2D device + context on top of the same GPU device.
        using ID2D1Factory1 d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
        _d2dDevice = d2dFactory.CreateDevice(_dxgiDevice);
        _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

        // Composition swap chain (no HWND association — DirectComposition owns presentation).
        using IDXGIAdapter adapter = _dxgiDevice.GetAdapter();
        using IDXGIFactory2 dxgiFactory = adapter.GetParent<IDXGIFactory2>();

        var swapChainDescription = new SwapChainDescription1
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
            Flags = SwapChainFlags.None,
        };

        _swapChain = dxgiFactory.CreateSwapChainForComposition(_d3dDevice, swapChainDescription);

        CreateTargetBitmap();

        // DirectComposition: device -> target (bound to HWND) -> visual (content = swap chain).
        // CheckError() throws on failure, so the out values are non-null past these calls.
        DComp.DCompositionCreateDevice(_dxgiDevice, out IDCompositionDevice? device).CheckError();
        _dcompDevice = device!;
        _dcompDevice.CreateTargetForHwnd(windowHandle, true, out IDCompositionTarget? target).CheckError();
        _dcompTarget = target!;
        _dcompVisual = _dcompDevice.CreateVisual()!;
        _dcompVisual.SetContent(_swapChain);
        _dcompTarget.SetRoot(_dcompVisual);
        _dcompDevice.Commit();
    }

    private void CreateTargetBitmap()
    {
        using IDXGISurface backBuffer = _swapChain.GetBuffer<IDXGISurface>(0);

        var bitmapProperties = new BitmapProperties1(
            new DCommon.PixelFormat(Format.B8G8R8A8_UNorm, DCommon.AlphaMode.Premultiplied),
            96f,
            96f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);

        _targetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(backBuffer, bitmapProperties);
        _d2dContext.Target = _targetBitmap;
    }
}
