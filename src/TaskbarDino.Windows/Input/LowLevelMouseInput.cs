using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Platform;
using TaskbarDino.Core.Primitives;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Windows.Input;

/// <summary>
/// Global pointer input via a <c>WH_MOUSE_LL</c> hook. Because it sees every mouse event
/// system-wide, the overlay window can stay fully click-through (never blocking the desktop): this
/// hook decides — per pixel — whether the pointer is over the dino, drives gaze on every move
/// (so it feels instant), and <b>consumes</b> only the clicks/drags that land on the dino, letting
/// everything else pass through untouched. The hook runs on the UI thread while the loop pumps.
/// </summary>
public sealed unsafe class LowLevelMouseInput : IInputSource
{
    private const int DragThreshold = 5;

    private readonly ILogger<LowLevelMouseInput> _logger;
    private readonly HOOKPROC _proc; // held so the native thunk survives GC
    private readonly HINSTANCE _hInstance;
    private readonly long _doubleClickMs;

    private HHOOK _hook;
    private Func<PixelPoint, bool>? _hitTest;

    private bool _overDino;
    private bool _potentialDrag;
    private bool _dragging;
    private PixelPoint _downScreen;
    private long _lastClickTick;

    public LowLevelMouseInput(ILogger<LowLevelMouseInput> logger)
    {
        _logger = logger;
        _proc = HookProc;
        _hInstance = new HINSTANCE(Marshal.GetHINSTANCE(typeof(LowLevelMouseInput).Module));
        _doubleClickMs = PInvoke.GetDoubleClickTime();
    }

    public event EventHandler<PixelPoint>? PointerHover;

    public event EventHandler? PointerLeft;

    public event EventHandler<PixelPoint>? Clicked;

    public event EventHandler<PixelPoint>? DoubleClicked;

    public event EventHandler<PixelPoint>? RightClicked;

    public event EventHandler<PixelPoint>? DragStarted;

    public event EventHandler<PixelPoint>? DragMoved;

    public event EventHandler<PixelPoint>? DragReleased;

    public void Start()
    {
        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _proc, _hInstance, 0);
        if (_hook.IsNull)
        {
            _logger.LogWarning("Failed to install the low-level mouse hook; dino interaction disabled.");
        }
        else
        {
            _logger.LogInformation("Low-level mouse hook installed.");
        }
    }

    public void SetHitTest(Func<PixelPoint, bool>? hitTest) => _hitTest = hitTest;

    public void Dispose()
    {
        if (!_hook.IsNull)
        {
            _ = PInvoke.UnhookWindowsHookEx(_hook);
            _hook = default;
        }
    }

    private LRESULT HookProc(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            var data = (MSLLHOOKSTRUCT*)lParam.Value;
            var screen = new PixelPoint(data->pt.X, data->pt.Y);
            if (Handle((uint)wParam.Value, screen))
            {
                return new LRESULT(1); // consume: keep this event from reaching the app beneath the dino
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    /// <summary>Processes one mouse event; returns <see langword="true"/> to suppress it.</summary>
    private bool Handle(uint message, PixelPoint screen)
    {
        bool over = _hitTest?.Invoke(screen) ?? false;

        switch (message)
        {
            case PInvoke.WM_MOUSEMOVE:
                if (_dragging)
                {
                    DragMoved?.Invoke(this, screen);
                }
                else if (_potentialDrag && (Math.Abs(screen.X - _downScreen.X) > DragThreshold || Math.Abs(screen.Y - _downScreen.Y) > DragThreshold))
                {
                    _dragging = true;
                    DragStarted?.Invoke(this, screen);
                }
                else if (over)
                {
                    _overDino = true;
                    PointerHover?.Invoke(this, screen); // every move → instant gaze
                }
                else if (_overDino)
                {
                    _overDino = false;
                    PointerLeft?.Invoke(this, EventArgs.Empty);
                }

                return false; // never suppress movement

            case PInvoke.WM_LBUTTONDOWN when over:
                _potentialDrag = true;
                _dragging = false;
                _downScreen = screen;
                return true;

            case PInvoke.WM_LBUTTONUP when _potentialDrag:
                if (_dragging)
                {
                    DragReleased?.Invoke(this, screen);
                }
                else
                {
                    RaiseClickOrDouble(screen);
                }

                _potentialDrag = false;
                _dragging = false;
                return true;

            case PInvoke.WM_RBUTTONDOWN when over:
                return true;

            case PInvoke.WM_RBUTTONUP when over:
                RightClicked?.Invoke(this, screen);
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
            DoubleClicked?.Invoke(this, screen);
        }
        else
        {
            _lastClickTick = now;
            Clicked?.Invoke(this, screen);
        }
    }
}
