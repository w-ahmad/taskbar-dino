using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Win32;

/// <summary>
/// Game-Mode keyboard input via a <c>WH_KEYBOARD_LL</c> hook that exists <b>only while a game is
/// running</b> — the overlay never takes focus, so the user's app keeps it, and outside the game no
/// key is observed or altered. During play the game keys (Space/↑/Enter = jump, ↓ = duck, Esc =
/// exit) are consumed so they do not leak into the app underneath; every other key passes through.
/// </summary>
internal sealed unsafe class KeyboardHook : IDisposable
{
    private const uint VkEscape = 0x1B;
    private const uint VkSpace = 0x20;
    private const uint VkEnter = 0x0D;
    private const uint VkUp = 0x26;
    private const uint VkDown = 0x28;

    private static KeyboardHook? _instance;

    private HHOOK _hook;
    private bool _jumpHeld;
    private bool _duckHeld;

    /// <summary>Jump key pressed (<see langword="true"/>) or released — hold to jump higher.</summary>
    public event Action<bool>? JumpChanged;

    /// <summary>Down arrow pressed/released.</summary>
    public event Action<bool>? DuckChanged;

    /// <summary>Esc pressed — leave Game Mode.</summary>
    public event Action? ExitPressed;

    public void Enable()
    {
        if (!_hook.IsNull)
        {
            return;
        }

        _instance = this;
        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, &HookProc, HINSTANCE.Null, 0);
        if (_hook.IsNull)
        {
            Log.Warn($"Keyboard hook failed ({Marshal.GetLastPInvokeError()}); game keys disabled.");
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
        _jumpHeld = _duckHeld = false;
        _instance = null;
    }

    public void Dispose() => Disable();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT HookProc(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0 && _instance is { } self)
        {
            var data = (KBDLLHOOKSTRUCT*)lParam.Value;
            try
            {
                if (self.Handle((uint)wParam.Value, data->vkCode))
                {
                    return new LRESULT(1);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "keyboard hook");
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    /// <summary>Processes one key event; returns <see langword="true"/> to suppress it.</summary>
    private bool Handle(uint message, uint vkCode)
    {
        bool down = message is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;

        switch (vkCode)
        {
            case VkSpace:
            case VkUp:
            case VkEnter:
                if (down != _jumpHeld) // key auto-repeat sends many downs; report edges only
                {
                    _jumpHeld = down;
                    JumpChanged?.Invoke(down);
                }

                return true;

            case VkDown:
                if (down != _duckHeld)
                {
                    _duckHeld = down;
                    DuckChanged?.Invoke(down);
                }

                return true;

            case VkEscape:
                if (down)
                {
                    ExitPressed?.Invoke();
                }

                return true;

            default:
                return false;
        }
    }
}
