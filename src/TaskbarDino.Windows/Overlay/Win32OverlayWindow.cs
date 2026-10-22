using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Platform;
using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Windows.Overlay;

/// <summary>
/// The transparent, top-most, tool-window overlay the dino is drawn into. It spans the whole virtual
/// desktop and is <b>always click-through</b> (<c>WS_EX_TRANSPARENT</c>), so it never blocks the
/// desktop or taskbar — pointer interaction with the dino is handled out-of-band by a global mouse
/// hook (<see cref="IInputSource"/>). It never activates (<c>WS_EX_NOACTIVATE</c>) and stays out of
/// Alt+Tab (<c>WS_EX_TOOLWINDOW</c>); DirectComposition gives it per-pixel transparency
/// (<c>WS_EX_NOREDIRECTIONBITMAP</c>).
/// </summary>
public sealed unsafe class Win32OverlayWindow : IOverlayWindow
{
    private const int QuitHotkeyId = 1;

    private static int _classCounter;

    private readonly ILogger<Win32OverlayWindow> _logger;
    private readonly string _className = $"TaskbarDinoOverlay_{Interlocked.Increment(ref _classCounter)}";
    private readonly WNDPROC _wndProc;
    private readonly HINSTANCE _hInstance;

    private HWND _hwnd;
    private PixelRect _bounds;
    private bool _visible;

    public Win32OverlayWindow(ILogger<Win32OverlayWindow> logger)
    {
        _logger = logger;
        _wndProc = WndProc;
        _hInstance = new HINSTANCE(Marshal.GetHINSTANCE(typeof(Win32OverlayWindow).Module));
        Create();
    }

    public nint Handle => _hwnd;

    public PixelRect Bounds => _bounds;

    public bool IsVisible => _visible;

    public uint Dpi => _hwnd.IsNull ? 96u : PInvoke.GetDpiForWindow(_hwnd);

    public event EventHandler<uint>? DpiChanged;

    public event EventHandler? CloseRequested;

    public event EventHandler? AccentColorChanged;

    public void SetBounds(PixelRect bounds)
    {
        _bounds = bounds;
        PInvoke.SetWindowPos(_hwnd, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
    }

    public void SetClickThrough(bool clickThrough)
    {
        var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        exStyle = clickThrough ? exStyle | WINDOW_EX_STYLE.WS_EX_TRANSPARENT : exStyle & ~WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        _ = PInvoke.SetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)exStyle);
    }

    public void Show()
    {
        _ = PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        _visible = true;
    }

    public void Hide()
    {
        _ = PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        _visible = false;
    }

    public void Dispose()
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        _ = PInvoke.UnregisterHotKey(_hwnd, QuitHotkeyId);
        _ = PInvoke.DestroyWindow(_hwnd);
        _hwnd = HWND.Null;

        fixed (char* classNamePtr = _className)
        {
            _ = PInvoke.UnregisterClass(new PCWSTR(classNamePtr), _hInstance);
        }
    }

    private static HWND HwndTopmost => new(-1);

    private void Create()
    {
        fixed (char* classNamePtr = _className)
        {
            var wndClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _wndProc,
                hInstance = _hInstance,
                lpszClassName = new PCWSTR(classNamePtr),
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
            };

            if (PInvoke.RegisterClassEx(in wndClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register overlay window class.");
            }

            const WINDOW_EX_STYLE exStyle =
                WINDOW_EX_STYLE.WS_EX_TOPMOST |
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |
                WINDOW_EX_STYLE.WS_EX_NOACTIVATE |
                WINDOW_EX_STYLE.WS_EX_TRANSPARENT |
                WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP;

            _hwnd = PInvoke.CreateWindowEx(exStyle, new PCWSTR(classNamePtr), default, WINDOW_STYLE.WS_POPUP, 0, 0, 16, 16, HWND.Null, HMENU.Null, _hInstance, null);
        }

        if (_hwnd.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create overlay window.");
        }

        _ = PInvoke.RegisterHotKey(_hwnd, QuitHotkeyId, HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_ALT | HOT_KEY_MODIFIERS.MOD_SHIFT | HOT_KEY_MODIFIERS.MOD_NOREPEAT, 'Q');
        _logger.LogInformation("Overlay window created (hwnd=0x{Handle:X}).", (nint)_hwnd);
    }

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case PInvoke.WM_HOTKEY:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return new LRESULT(0);

            case PInvoke.WM_DPICHANGED:
                DpiChanged?.Invoke(this, (uint)(wParam.Value & 0xFFFF));
                return new LRESULT(0);

            case WmDwmColorizationColorChanged:
                AccentColorChanged?.Invoke(this, EventArgs.Empty);
                return new LRESULT(0);

            case WmSettingChange:
                if (IsImmersiveColorSet(lParam))
                {
                    AccentColorChanged?.Invoke(this, EventArgs.Empty);
                }

                break; // still let DefWindowProc run

            case PInvoke.WM_CLOSE:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private const uint WmSettingChange = 0x001A;
    private const uint WmDwmColorizationColorChanged = 0x0320;

    private static bool IsImmersiveColorSet(LPARAM lParam)
    {
        if (lParam.Value == 0)
        {
            return false;
        }

        string? area = Marshal.PtrToStringUni(lParam.Value);
        return string.Equals(area, "ImmersiveColorSet", StringComparison.Ordinal);
    }
}
