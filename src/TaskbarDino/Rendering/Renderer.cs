using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;

using TaskbarDino.Win32;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace TaskbarDino.Rendering;

/// <summary>A sprite sheet uploaded to the GPU.</summary>
internal sealed unsafe class Texture : IDisposable
{
    internal ID2D1Bitmap1* Bitmap;

    public void Dispose() => Com.Release(ref Bitmap);
}

/// <summary>
/// GPU renderer for the transparent overlay: a Direct3D 11 device, a DXGI composition swap chain
/// (premultiplied BGRA), DirectComposition binding that swap chain to the window so the DWM
/// composites it with true per-pixel transparency, and a Direct2D context drawing sprites with
/// nearest-neighbour sampling so pixel art stays crisp at any size. Everything is raw COM through
/// CsWin32 — no runtime dependencies. Presents with vsync, so a frame never costs more than one
/// refresh; the loop decides <em>whether</em> a frame is needed at all.
/// </summary>
internal sealed unsafe class Renderer : IDisposable
{
    private ID3D11Device* _device;
    private IDXGIDevice* _dxgiDevice;
    private IDXGISwapChain1* _swapChain;
    private ID2D1Device* _d2dDevice;
    private ID2D1DeviceContext* _context;
    private ID2D1Bitmap1* _target;
    private IDCompositionDevice* _dcompDevice;
    private IDCompositionTarget* _dcompTarget;
    private IDCompositionVisual* _visual;

    public Renderer(HWND window, int width, int height, bool software)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Software = software || ForceWarp;
        try
        {
            Initialize(window);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Environment override (<c>TASKBARDINO_WARP=1</c>) for the software rasteriser, handy for measurements.</summary>
    private static bool ForceWarp { get; } = Environment.GetEnvironmentVariable("TASKBARDINO_WARP") == "1";

    /// <summary>True when drawing with WARP (by setting, override, or because no GPU device exists).</summary>
    public bool Software { get; private set; }

    /// <summary>Linear instead of nearest-neighbour sampling — only for downscaled sprites, where dropped pixels would break lines.</summary>
    public bool Smooth { get; set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Set when a present failed because the GPU device was lost; the owner recreates the renderer.</summary>
    public bool DeviceLost { get; private set; }

    public Texture CreateTexture(SpriteSheet sheet)
    {
        var texture = new Texture();
        Upload(texture, sheet);
        return texture;
    }

    /// <summary>Replaces a texture's pixels (theme change).</summary>
    public void Upload(Texture texture, SpriteSheet sheet)
    {
        D2D1_BITMAP_PROPERTIES1 props = BitmapProperties(D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE);
        ID2D1Bitmap1* bitmap;
        fixed (Bgra32* p = sheet.Pixels)
        {
            _context->CreateBitmap(new D2D_SIZE_U { width = (uint)sheet.Width, height = (uint)sheet.Height }, p, (uint)(sheet.Width * 4), &props, &bitmap);
        }

        Com.Release(ref texture.Bitmap);
        texture.Bitmap = bitmap;
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        _context->SetTarget(null);
        Com.Release(ref _target);
        _swapChain->ResizeBuffers(0, (uint)Width, (uint)Height, DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, 0);
        CreateTargetBitmap();
    }

    public void Begin()
    {
        _context->BeginDraw();
        var clear = new D2D1_COLOR_F { r = 0f, g = 0f, b = 0f, a = 0f };
        _context->Clear(&clear);
    }

    public void Draw(Texture texture, in AtlasRegion source, in RectF destination, bool flipX = false)
    {
        PixelRect s = source.Source;
        var src = new D2D_RECT_F { left = s.X, top = s.Y, right = s.Right, bottom = s.Bottom };
        var dst = new D2D_RECT_F { left = destination.X, top = destination.Y, right = destination.Right, bottom = destination.Bottom };

        if (flipX)
        {
            // Mirror horizontally about the destination's centre: x' = -x + 2·cx.
            D2D_MATRIX_3X2_F m = default;
            m.m11 = -1f;
            m.m22 = 1f;
            m.dx = 2f * destination.CenterX;
            _context->SetTransform(&m);
        }

        _context->DrawBitmap((ID2D1Bitmap*)texture.Bitmap, &dst, 1f, Smooth ? D2D1_BITMAP_INTERPOLATION_MODE.D2D1_BITMAP_INTERPOLATION_MODE_LINEAR : D2D1_BITMAP_INTERPOLATION_MODE.D2D1_BITMAP_INTERPOLATION_MODE_NEAREST_NEIGHBOR, &src);

        if (flipX)
        {
            D2D_MATRIX_3X2_F identity = default;
            identity.m11 = 1f;
            identity.m22 = 1f;
            _context->SetTransform(&identity);
        }
    }

    /// <summary>Ends the frame and presents it (vsync). Returns <see langword="false"/> if the device was lost.</summary>
    public bool End()
    {
        HRESULT hr = _context->EndDraw(null, null);
        if (hr.Succeeded)
        {
            hr = _swapChain->Present(1, (DXGI_PRESENT)0);
        }

        if (hr.Failed)
        {
            DeviceLost = hr == HRESULT.DXGI_ERROR_DEVICE_REMOVED || hr == HRESULT.DXGI_ERROR_DEVICE_RESET || hr.Value == unchecked((int)0x8899000C) /* D2DERR_RECREATE_TARGET */;
            Log.Warn($"Present failed (0x{hr.Value:X8}); device lost = {DeviceLost}.");
            return !DeviceLost;
        }

        return true;
    }

    public void Dispose()
    {
        Com.Release(ref _target);
        Com.Release(ref _visual);
        Com.Release(ref _dcompTarget);
        Com.Release(ref _dcompDevice);
        Com.Release(ref _context);
        Com.Release(ref _d2dDevice);
        Com.Release(ref _swapChain);
        Com.Release(ref _dxgiDevice);
        Com.Release(ref _device);
    }

    private void Initialize(HWND window)
    {
        ReadOnlySpan<D3D_FEATURE_LEVEL> levels = [D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0];
        // Single-threaded + no internal driver threading: the app draws a handful of sprites from one
        // thread, so the driver's worker-thread pool would be pure overhead (threads + memory).
        const D3D11_CREATE_DEVICE_FLAG flags =
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT |
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_SINGLETHREADED |
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_PREVENT_INTERNAL_THREADING_OPTIMIZATIONS;

        ID3D11Device* device = null;
        HRESULT hr = new(unchecked((int)0x80004005)); // E_FAIL until a device exists
        if (!Software)
        {
            hr = PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, HMODULE.Null, flags, levels, PInvoke.D3D11_SDK_VERSION, &device, out _, null);
            if (hr.Failed)
            {
                Log.Warn($"Hardware Direct3D unavailable (0x{hr.Value:X8}); using WARP.");
            }
        }

        if (hr.Failed)
        {
            Software = true;
            PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, HMODULE.Null, flags, levels, PInvoke.D3D11_SDK_VERSION, &device, out _, null).ThrowOnFailure();
        }

        _device = device;
        _dxgiDevice = Com.Query<IDXGIDevice>(_device, in IDXGIDevice.IID_Guid);

        // Direct2D device + context on the same GPU device.
        void* factoryRaw;
        var options = new D2D1_FACTORY_OPTIONS { debugLevel = D2D1_DEBUG_LEVEL.D2D1_DEBUG_LEVEL_NONE };
        PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, in ID2D1Factory1.IID_Guid, options, out factoryRaw).ThrowOnFailure();
        var factory = (ID2D1Factory1*)factoryRaw;
        try
        {
            ID2D1Device* d2dDevice;
            factory->CreateDevice(_dxgiDevice, &d2dDevice);
            _d2dDevice = d2dDevice;
        }
        finally
        {
            factory->Release();
        }

        ID2D1DeviceContext* context;
        _d2dDevice->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS.D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &context);
        _context = context;
        _context->SetDpi(96f, 96f); // draw in physical pixels; the app scales sprites itself

        // Composition swap chain (no HWND association — DirectComposition owns presentation).
        IDXGIAdapter* adapter;
        _dxgiDevice->GetAdapter(&adapter);
        IDXGIFactory2* dxgiFactory;
        try
        {
            void* parent;
            Guid factoryIid = IDXGIFactory2.IID_Guid;
            adapter->GetParent(&factoryIid, &parent);
            dxgiFactory = (IDXGIFactory2*)parent;
        }
        finally
        {
            adapter->Release();
        }

        try
        {
            var desc = new DXGI_SWAP_CHAIN_DESC1
            {
                Width = (uint)Width,
                Height = (uint)Height,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                Stereo = false,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                BufferUsage = DXGI_USAGE.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                BufferCount = 2,
                Scaling = DXGI_SCALING.DXGI_SCALING_STRETCH,
                SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL,
                AlphaMode = DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_PREMULTIPLIED,
                Flags = 0,
            };

            IDXGISwapChain1* swapChain;
            dxgiFactory->CreateSwapChainForComposition((Windows.Win32.System.Com.IUnknown*)_device, &desc, null, &swapChain);
            _swapChain = swapChain;
        }
        finally
        {
            dxgiFactory->Release();
        }

        CreateTargetBitmap();

        // DirectComposition: device → target (bound to the HWND) → visual (content = swap chain).
        IDCompositionDevice* dcomp;
        PInvoke.DCompositionCreateDevice(_dxgiDevice, out dcomp).ThrowOnFailure();
        _dcompDevice = dcomp;

        IDCompositionTarget* target;
        _dcompDevice->CreateTargetForHwnd(window, true, &target);
        _dcompTarget = target;

        IDCompositionVisual* visual;
        _dcompDevice->CreateVisual(&visual);
        _visual = visual;
        _visual->SetContent((Windows.Win32.System.Com.IUnknown*)_swapChain);
        _dcompTarget->SetRoot(_visual);
        _dcompDevice->Commit();
    }

    private void CreateTargetBitmap()
    {
        void* surfaceRaw;
        Guid surfaceIid = IDXGISurface.IID_Guid;
        _swapChain->GetBuffer(0, &surfaceIid, &surfaceRaw);
        var surface = (IDXGISurface*)surfaceRaw;
        try
        {
            D2D1_BITMAP_PROPERTIES1 props = BitmapProperties(D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_CANNOT_DRAW);
            ID2D1Bitmap1* target;
            _context->CreateBitmapFromDxgiSurface(surface, &props, &target);
            _target = target;
            _context->SetTarget((ID2D1Image*)_target);
        }
        finally
        {
            surface->Release();
        }
    }

    private static D2D1_BITMAP_PROPERTIES1 BitmapProperties(D2D1_BITMAP_OPTIONS options) => new()
    {
        pixelFormat = new D2D1_PIXEL_FORMAT { format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED },
        dpiX = 96f,
        dpiY = 96f,
        bitmapOptions = options,
        colorContext = null,
    };
}
