using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Win32;

/// <summary>
/// The transparent, top-most tool window the dino is drawn into. It is small (it hugs the dino and
/// is repositioned to follow it), <b>always click-through</b> (<c>WS_EX_TRANSPARENT</c>) so it never
/// blocks the taskbar — pointer interaction comes from the mouse hook instead — never activates
/// (<c>WS_EX_NOACTIVATE</c>), stays out of Alt+Tab (<c>WS_EX_TOOLWINDOW</c>) and composes through
/// DirectComposition (<c>WS_EX_NOREDIRECTIONBITMAP</c>). It is also the app's system-message sink.
/// </summary>
internal sealed unsafe class OverlayWindow : IDisposable
{
    public const string ClassName = "TaskbarDinoOverlay";

    /// <summary>Posted by a second launch: "show yourself" (un-hides a hidden dino).</summary>
    public const uint ShowRequestMessage = PInvoke.WM_APP + 2;

    private static OverlayWindow? _instance; // the window procedure is static (UnmanagedCallersOnly)

    private readonly HINSTANCE _hInstance;
    private readonly uint _taskbarCreatedMessage;
    private HWND _hwnd;
    private PixelRect _bounds;

    public OverlayWindow()
    {
        _instance = this;
        _hInstance = new HINSTANCE(PInvoke.GetModuleHandle((PCWSTR)null).Value);
        fixed (char* name = "TaskbarCreated")
        {
            _taskbarCreatedMessage = PInvoke.RegisterWindowMessage(name);
        }

        Create();
    }

    public HWND Handle => _hwnd;

    public PixelRect Bounds => _bounds;

    public bool IsVisible { get; private set; }

    /// <summary>The OS asked us to close (session end, task manager…).</summary>
    public event Action? CloseRequested;

    /// <summary>Monitors/resolution/work-area/DPI changed, or Explorer restarted — positions must be recomputed.</summary>
    public event Action? LayoutChanged;

    /// <summary>Explorer (and the taskbar) was recreated — the tray icon must be re-added and z-order re-asserted.</summary>
    public event Action? TaskbarCreated;

    /// <summary>The Windows accent colour changed.</summary>
    public event Action? AccentColorChanged;

    /// <summary>The system resumed from sleep.</summary>
    public event Action? Resumed;

    /// <summary>Another instance was launched — the user wants the dino visible.</summary>
    public event Action? ShowRequested;

    public void SetBounds(PixelRect bounds)
    {
        _bounds = bounds;
        _ = PInvoke.SetWindowPos(_hwnd, HWND.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
    }

    /// <summary>Re-asserts top-most z-order (after Explorer restarts the taskbar above us).</summary>
    public void BringToTop() =>
        PInvoke.SetWindowPos(_hwnd, HWND.HWND_TOPMOST, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);

    public void Show()
    {
        if (!IsVisible)
        {
            _ = PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            IsVisible = true;
        }
    }

    public void Hide()
    {
        if (IsVisible)
        {
            _ = PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
            IsVisible = false;
        }
    }

    public void Dispose()
    {
        if (!_hwnd.IsNull)
        {
            _ = PInvoke.DestroyWindow(_hwnd);
            _hwnd = HWND.Null;
        }

        fixed (char* name = ClassName)
        {
            _ = PInvoke.UnregisterClass(name, _hInstance);
        }

        _instance = null;
    }

    private void Create()
    {
        fixed (char* name = ClassName)
        {
            var wndClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = _hInstance,
                lpszClassName = name,
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
            };

            if (PInvoke.RegisterClassEx(&wndClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to register the overlay window class.");
            }

            const WINDOW_EX_STYLE exStyle =
                WINDOW_EX_STYLE.WS_EX_TOPMOST |
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |
                WINDOW_EX_STYLE.WS_EX_NOACTIVATE |
                WINDOW_EX_STYLE.WS_EX_TRANSPARENT |
                WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP;

            _hwnd = PInvoke.CreateWindowEx(exStyle, name, (PCWSTR)null, WINDOW_STYLE.WS_POPUP, 0, 0, 16, 16, HWND.Null, HMENU.Null, _hInstance, null);
        }

        if (_hwnd.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to create the overlay window.");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        OverlayWindow? self = _instance;
        if (self is not null)
        {
            try
            {
                if (self.HandleMessage(msg, wParam, lParam))
                {
                    return new LRESULT(0);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "overlay window procedure");
            }
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private bool HandleMessage(uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case PInvoke.WM_CLOSE:
                CloseRequested?.Invoke();
                return true;

            case PInvoke.WM_DPICHANGED:
            case PInvoke.WM_DISPLAYCHANGE:
                LayoutChanged?.Invoke();
                return true;

            case PInvoke.WM_SETTINGCHANGE:
                if (wParam.Value == (nuint)SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA)
                {
                    LayoutChanged?.Invoke();
                }
                else if (lParam.Value != 0 && Marshal.PtrToStringUni(lParam.Value) == "ImmersiveColorSet")
                {
                    AccentColorChanged?.Invoke();
                }

                return false;

            case PInvoke.WM_DWMCOLORIZATIONCOLORCHANGED:
                AccentColorChanged?.Invoke();
                return true;

            case PInvoke.WM_POWERBROADCAST:
                if (wParam.Value is PInvoke.PBT_APMRESUMEAUTOMATIC or PInvoke.PBT_APMRESUMESUSPEND)
                {
                    Resumed?.Invoke();
                }

                return false;

            case ShowRequestMessage:
                ShowRequested?.Invoke();
                return true;

            default:
                if (msg == _taskbarCreatedMessage)
                {
                    TaskbarCreated?.Invoke();
                    return true;
                }

                return false;
        }
    }
}
