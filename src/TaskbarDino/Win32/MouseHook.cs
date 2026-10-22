using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Win32;

/// <summary>
/// Pointer input via a <c>WH_MOUSE_LL</c> hook. Because it sees every mouse event system-wide, the
/// overlay can stay fully click-through: the hook asks the app whether the pointer is over the dino
/// and <b>consumes</b> only the clicks/drags that land on it, letting everything else pass through
/// untouched. The hook runs on the UI thread while the loop pumps, so handlers need no locking.
/// The per-event cost is one cheap rectangle test.
/// </summary>
internal sealed unsafe class MouseHook : IDisposable
{
    private const int DragThreshold = 5;

    private static MouseHook? _instance;

    private readonly long _doubleClickMs = PInvoke.GetDoubleClickTime();
    private HHOOK _hook;

    private bool _overDino;
    private bool _potentialDrag;
    private bool _dragging;
    private PixelPoint _downScreen;
    private long _lastClickTick;

    /// <summary>"Is this screen point on the dino?" — supplied by the app.</summary>
    public Func<PixelPoint, bool>? HitTest { get; set; }

    public event Action<PixelPoint>? PointerHover;

    public event Action? PointerLeft;

    public event Action<PixelPoint>? Clicked;

    public event Action<PixelPoint>? DoubleClicked;

    public event Action<PixelPoint>? RightClicked;

    public event Action<PixelPoint>? DragStarted;

    public event Action<PixelPoint>? DragMoved;

    public event Action<PixelPoint>? DragReleased;

    public void Start()
    {
        _instance = this;
        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, &HookProc, HINSTANCE.Null, 0);
        if (_hook.IsNull)
        {
            Log.Warn($"Mouse hook failed ({Marshal.GetLastPInvokeError()}); dino interaction disabled.");
        }
    }

    /// <summary>Forces any in-progress drag/hover to end (e.g. when the dino hides).</summary>
    public void Reset()
    {
        _potentialDrag = false;
        _dragging = false;
        _overDino = false;
    }

    public void Dispose()
    {
        if (!_hook.IsNull)
        {
            _ = PInvoke.UnhookWindowsHookEx(_hook);
            _hook = default;
        }

        _instance = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT HookProc(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0 && _instance is { } self)
        {
            var data = (MSLLHOOKSTRUCT*)lParam.Value;
            try
            {
                if (self.Handle((uint)wParam.Value, new PixelPoint(data->pt.X, data->pt.Y)))
                {
                    return new LRESULT(1); // consume: keep this event from reaching the app beneath the dino
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "mouse hook");
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    /// <summary>Processes one mouse event; returns <see langword="true"/> to suppress it.</summary>
    private bool Handle(uint message, PixelPoint screen)
    {
        switch (message)
        {
            case PInvoke.WM_MOUSEMOVE:
                if (_dragging)
                {
                    DragMoved?.Invoke(screen);
                }
                else if (_potentialDrag)
                {
                    if (Math.Abs(screen.X - _downScreen.X) > DragThreshold || Math.Abs(screen.Y - _downScreen.Y) > DragThreshold)
                    {
                        _dragging = true;
                        DragStarted?.Invoke(screen);
                    }
                }
                else if (HitTest?.Invoke(screen) ?? false)
                {
                    _overDino = true;
                    PointerHover?.Invoke(screen);
                }
                else if (_overDino)
                {
                    _overDino = false;
                    PointerLeft?.Invoke();
                }

                return false; // never suppress movement

            case PInvoke.WM_LBUTTONDOWN when HitTest?.Invoke(screen) ?? false:
                _potentialDrag = true;
                _dragging = false;
                _downScreen = screen;
                return true;

            case PInvoke.WM_LBUTTONUP when _potentialDrag:
                _potentialDrag = false;
                if (_dragging)
                {
                    _dragging = false;
                    DragReleased?.Invoke(screen);
                }
                else
                {
                    RaiseClickOrDouble(screen);
                }

                return true;

            case PInvoke.WM_RBUTTONDOWN when HitTest?.Invoke(screen) ?? false:
                return true;

            case PInvoke.WM_RBUTTONUP when HitTest?.Invoke(screen) ?? false:
                RightClicked?.Invoke(screen);
                return true;

            default:
                return false;
        }
    }

    private void RaiseClickOrDouble(PixelPoint screen)
    {
        long now = Environment.TickCount64;
        if (now - _lastClickTick <= _doubleClickMs)
        {
            _lastClickTick = 0;
            DoubleClicked?.Invoke(screen);
        }
        else
        {
            _lastClickTick = now;
            Clicked?.Invoke(screen);
        }
    }
}
