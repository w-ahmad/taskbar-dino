using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Platform;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Windows.Input;

/// <summary>
/// Game-Mode keyboard input via a global <c>WH_KEYBOARD_LL</c> hook — the keyboard mirror of
/// <see cref="LowLevelMouseInput"/>. Installed only while a game is running so it never adds overhead
/// (or intercepts keys) at other times. Space/↑/Esc are consumed so they never reach the app beneath
/// the overlay; every other key passes through. Jump is reported as a combined held state (Space OR ↑)
/// so holding gives a higher hop. Runs on the UI thread while the loop pumps, so events are thread-safe.
/// </summary>
public sealed unsafe class LowLevelKeyboardInput : IGameInput
{
    private const uint VkEscape = 0x1B;
    private const uint VkSpace = 0x20;
    private const uint VkUp = 0x26;

    private readonly ILogger<LowLevelKeyboardInput> _logger;
    private readonly HOOKPROC _proc; // held so the native thunk survives GC
    private readonly HINSTANCE _hInstance;

    private HHOOK _hook;
    private bool _spaceDown;
    private bool _upDown;

    public LowLevelKeyboardInput(ILogger<LowLevelKeyboardInput> logger)
    {
        _logger = logger;
        _proc = HookProc;
        _hInstance = new HINSTANCE(Marshal.GetHINSTANCE(typeof(LowLevelKeyboardInput).Module));
    }

    public event EventHandler<bool>? JumpChanged;

    public event EventHandler? ExitPressed;

    public void Enable()
    {
        if (!_hook.IsNull)
        {
            return;
        }

        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _proc, _hInstance, 0);
        if (_hook.IsNull)
        {
            _logger.LogWarning("Failed to install the low-level keyboard hook; game keys disabled.");
        }
    }

    public void Disable()
    {
        if (_hook.IsNull)
        {
            return;
        }

        _ = PInvoke.UnhookWindowsHookEx(_hook);
        _hook = default;
        _spaceDown = _upDown = false;
    }

    public void Dispose() => Disable();

    private LRESULT HookProc(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            var data = (KBDLLHOOKSTRUCT*)lParam.Value;
            if (Handle((uint)wParam.Value, data->vkCode))
            {
                return new LRESULT(1); // consume: keep the game keys from reaching the app underneath
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    /// <summary>Processes one key event; returns <see langword="true"/> to suppress it.</summary>
    private bool Handle(uint message, uint vkCode)
    {
        bool down = message is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
        bool up = message is PInvoke.WM_KEYUP or PInvoke.WM_SYSKEYUP;

        switch (vkCode)
        {
            case VkSpace:
            case VkUp:
                bool wasHeld = _spaceDown || _upDown;
                if (vkCode == VkSpace)
                {
                    _spaceDown = down || (!up && _spaceDown);
                }
                else
                {
                    _upDown = down || (!up && _upDown);
                }

                bool held = _spaceDown || _upDown;
                if (held != wasHeld)
                {
                    JumpChanged?.Invoke(this, held);
                }

                return true;

            case VkEscape:
                if (down)
                {
                    ExitPressed?.Invoke(this, EventArgs.Empty);
                }

                return true;

            default:
                return false;
        }
    }
}
