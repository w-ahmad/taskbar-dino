using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Win32;

/// <summary>
/// Pointer input via a <c>WH_MOUSE_LL</c> hook. Because it sees every mouse event system-wide, the
/// overlay can stay fully click-through: the hook asks the app whether the pointer is over the dino
/// and <b>consumes</b> only the clicks/drags that land on it, letting everything else pass through
/// untouched. The hook runs on the UI thread while the loop pumps, so handlers need no locking.
/// A single click is reported only after the double-click interval has passed without a second
/// click (so a double-click never also plays the click reaction); the wait uses a thread timer.
/// </summary>
internal sealed unsafe class MouseHook : IDisposable
{
    private const int DragThreshold = 5;
    private const int VkLeftButton = 0x01;

    private static MouseHook? _instance;

    private readonly uint _doubleClickMs = PInvoke.GetDoubleClickTime();
    private readonly int _doubleClickDx = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDOUBLECLK) / 2;
    private readonly int _doubleClickDy = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDOUBLECLK) / 2;
    private HHOOK _hook;

    private bool _overDino;
    private bool _potentialDrag;
    private bool _dragging;
    private PixelPoint _downScreen;

    private nuint _clickTimer;       // pending single click, resolved when the double-click interval passes
    private PixelPoint _clickPoint;
    private long _clickTick;

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

    public bool IsDragging => _dragging;

    public void Start()
    {
        if (!_hook.IsNull)
        {
            return;
        }

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
        CancelPendingClick();
    }

    /// <summary>
    /// Safety net for a drag whose button-up never reached the hook (hook removed by Windows after a
    /// stall, input swallowed by a secure desktop): if the left button is no longer down, end the drag
    /// at the current cursor position. Returns <see langword="true"/> if a drag was ended this way.
    /// </summary>
    public bool EndDragIfButtonReleased()
    {
        if (!_dragging || (PInvoke.GetAsyncKeyState(VkLeftButton) & 0x8000) != 0)
        {
            return false;
        }

        System.Drawing.Point cursor;
        _ = PInvoke.GetCursorPos(&cursor);
        _potentialDrag = false;
        _dragging = false;
        Log.Warn($"Drag ended without a button-up event; releasing at the cursor.");
        DragReleased?.Invoke(new PixelPoint(cursor.X, cursor.Y));
        return true;
    }

    public void Dispose()
    {
        CancelPendingClick();
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
                        CancelPendingClick(); // a drag is not a click
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
                    OnClick(screen);
                }

                return true;

            case PInvoke.WM_RBUTTONDOWN when HitTest?.Invoke(screen) ?? false:
                return true;

            case PInvoke.WM_RBUTTONUP when HitTest?.Invoke(screen) ?? false:
                CancelPendingClick();
                RightClicked?.Invoke(screen);
                return true;

            default:
                return false;
        }
    }

    private void OnClick(PixelPoint screen)
    {
        long now = Environment.TickCount64;
        bool second = _clickTimer != 0 && now - _clickTick <= _doubleClickMs &&
                      Math.Abs(screen.X - _clickPoint.X) <= _doubleClickDx && Math.Abs(screen.Y - _clickPoint.Y) <= _doubleClickDy;
        if (second)
        {
            CancelPendingClick();
            DoubleClicked?.Invoke(screen);
            return;
        }

        // First click: hold it until the double-click interval passes, then report it as a single click.
        CancelPendingClick();
        _clickPoint = screen;
        _clickTick = now;
        _clickTimer = PInvoke.SetTimer(HWND.Null, 0, _doubleClickMs, &OnClickTimer);
        if (_clickTimer == 0)
        {
            Clicked?.Invoke(screen); // no timer available: report immediately
        }
    }

    private void CancelPendingClick()
    {
        if (_clickTimer != 0)
        {
            _ = PInvoke.KillTimer(HWND.Null, _clickTimer);
            _clickTimer = 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnClickTimer(HWND hwnd, uint message, nuint timerId, uint time)
    {
        if (_instance is not { } self || self._clickTimer != timerId)
        {
            _ = PInvoke.KillTimer(HWND.Null, timerId);
            return;
        }

        self.CancelPendingClick();
        try
        {
            self.Clicked?.Invoke(self._clickPoint);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "click");
        }
    }
}
