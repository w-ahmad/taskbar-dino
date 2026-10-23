using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using TaskbarDino.Core.Dino;
using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Settings;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Win32;

/// <summary>Commands the context menu can raise. Choice commands carry their value in the argument.</summary>
internal enum TrayCommand
{
    StartGame,
    HideOneHour,
    HideFourHours,
    ShowNow,
    Theme,
    Size,
    Activity,
    TogglePauseInFullscreen,
    ToggleSoftwareRendering,
    ToggleStartWithWindows,
    ResetHighScore,
    OpenSettingsFile,
    ResetSettings,
    Exit,
}

/// <summary>What the menu shows as checked / available; supplied by the app when the menu opens.</summary>
internal readonly record struct TrayMenuState(AppSettings Settings, bool Hidden, bool InGame);

/// <summary>
/// System-tray presence: a notification-area icon (built from the dino's own pixels) plus the
/// right-click context menu, which doubles as the whole settings UI — the smallest native UI that
/// fits a companion this small. Owns a hidden message window for the tray callback.
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const string ClassName = "TaskbarDinoTray";
    private const uint CallbackMessage = PInvoke.WM_APP + 1;
    private const uint IconId = 1;

    // Menu item ids
    private const uint IdStartGame = 1, IdHide1 = 2, IdHide4 = 3, IdShowNow = 4, IdFullscreen = 5, IdStartup = 6,
        IdResetHigh = 7, IdOpenSettings = 8, IdResetSettings = 9, IdExit = 10, IdSoftware = 11;
    private const uint IdThemeBase = 100, IdSizeBase = 200, IdActivityBase = 300;

    private const uint TpmRightButton = 0x0002, TpmReturnCmd = 0x0100, TpmBottomAlign = 0x0020;

    private static readonly (DinoTheme Theme, string Label)[] Themes =
    [
        (DinoTheme.Accent, "Windows Accent"),
        (DinoTheme.ChromeGray, "Chrome Gray"),
        (DinoTheme.Green, "Green"),
        (DinoTheme.Blue, "Blue"),
        (DinoTheme.Red, "Red"),
        (DinoTheme.Purple, "Purple"),
        (DinoTheme.Gold, "Gold"),
    ];

    private static readonly (DinoSize Size, string Label)[] Sizes =
    [
        (DinoSize.Tiny, "Tiny"),
        (DinoSize.Small, "Small"),
        (DinoSize.Medium, "Medium"),
        (DinoSize.Large, "Large"),
    ];

    private static readonly (ActivityLevel Level, string Label)[] Activities =
    [
        (ActivityLevel.Calm, "Calm"),
        (ActivityLevel.Normal, "Normal"),
        (ActivityLevel.Lively, "Lively"),
    ];

    private static TrayIcon? _instance;

    private readonly HINSTANCE _hInstance;
    private HWND _hwnd;
    private HICON _icon;
    private bool _installed;
    private bool _menuOpen;

    public TrayIcon()
    {
        _instance = this;
        _hInstance = new HINSTANCE(PInvoke.GetModuleHandle((PCWSTR)null).Value);
        CreateOwnerWindow();
    }

    /// <summary>Raised (on the UI thread) when a menu item is chosen; the int is the choice for Theme/Size/Activity.</summary>
    public event Action<TrayCommand, int>? Command;

    /// <summary>Asked when the menu is about to open, to reflect current settings.</summary>
    public Func<TrayMenuState>? StateProvider { get; set; }

    public void Install(ReadOnlySpan<Bgra32> iconPixels, int size, string tooltip)
    {
        ReplaceIcon(iconPixels, size);
        Notify(NOTIFY_ICON_MESSAGE.NIM_ADD, tooltip);
        _installed = true;
    }

    public void UpdateIcon(ReadOnlySpan<Bgra32> iconPixels, int size)
    {
        ReplaceIcon(iconPixels, size);
        if (_installed)
        {
            Notify(NOTIFY_ICON_MESSAGE.NIM_MODIFY, null);
        }
    }

    /// <summary>Re-adds the icon after Explorer restarts (the shell forgets all tray icons).</summary>
    public void Reinstall()
    {
        if (_installed)
        {
            Notify(NOTIFY_ICON_MESSAGE.NIM_ADD, "Taskbar Dino");
        }
    }

    /// <summary>Shows the context menu at the cursor. Modal; call from the UI thread, never from a hook.</summary>
    public void ShowMenu()
    {
        if (_menuOpen || StateProvider is null)
        {
            return;
        }

        _menuOpen = true;
        try
        {
            ShowMenuCore(StateProvider());
        }
        finally
        {
            _menuOpen = false;
        }
    }

    public void Dispose()
    {
        if (_installed)
        {
            var data = new NOTIFYICONDATAW { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = _hwnd, uID = IconId };
            _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
            _installed = false;
        }

        if (!_icon.IsNull)
        {
            _ = PInvoke.DestroyIcon(_icon);
            _icon = default;
        }

        if (!_hwnd.IsNull)
        {
            _ = PInvoke.DestroyWindow(_hwnd);
            _hwnd = HWND.Null;
            fixed (char* name = ClassName)
            {
                _ = PInvoke.UnregisterClass(name, _hInstance);
            }
        }

        _instance = null;
    }

    private void CreateOwnerWindow()
    {
        fixed (char* name = ClassName)
        {
            var wndClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = _hInstance,
                lpszClassName = name,
            };

            if (PInvoke.RegisterClassEx(&wndClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to register the tray window class.");
            }

            _hwnd = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, name, (PCWSTR)null, WINDOW_STYLE.WS_POPUP, 0, 0, 0, 0, HWND.Null, HMENU.Null, _hInstance, null);
        }

        if (_hwnd.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to create the tray owner window.");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == CallbackMessage && _instance is { } self)
        {
            uint mouseEvent = (uint)(lParam.Value & 0xFFFF);
            if (mouseEvent is PInvoke.WM_RBUTTONUP or PInvoke.WM_LBUTTONUP)
            {
                try
                {
                    self.ShowMenu();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "tray menu");
                }
            }

            return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Notify(NOTIFY_ICON_MESSAGE message, string? tooltip)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };

        if (tooltip is not null)
        {
            data.uFlags |= NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
            tooltip.AsSpan(0, Math.Min(tooltip.Length, 127)).CopyTo(data.szTip.AsSpan());
        }

        if (!PInvoke.Shell_NotifyIcon(message, &data))
        {
            Log.Warn($"Shell_NotifyIcon({message}) failed.");
        }
    }

    private void ShowMenuCore(TrayMenuState state)
    {
        AppSettings s = state.Settings;
        HMENU menu = PInvoke.CreatePopupMenu();
        HMENU colors = PInvoke.CreatePopupMenu();
        HMENU sizes = PInvoke.CreatePopupMenu();
        HMENU personality = PInvoke.CreatePopupMenu();

        foreach ((DinoTheme theme, string label) in Themes)
        {
            Append(colors, IdThemeBase + (uint)theme, label, theme == s.Theme);
        }

        foreach ((DinoSize size, string label) in Sizes)
        {
            Append(sizes, IdSizeBase + (uint)size, label, size == s.Size);
        }

        foreach ((ActivityLevel level, string label) in Activities)
        {
            Append(personality, IdActivityBase + (uint)level, label, level == s.Activity);
        }

        Append(menu, IdStartGame, state.InGame ? "Stop Game" : "Start Game");
        Separator(menu);
        if (state.Hidden)
        {
            Append(menu, IdShowNow, "Show Dino Now");
        }

        Append(menu, IdHide1, "Hide for 1 Hour");
        Append(menu, IdHide4, "Hide for 4 Hours");
        Separator(menu);
        Submenu(menu, colors, "Dino Color");
        Submenu(menu, sizes, "Dino Size");
        Submenu(menu, personality, "Personality");
        Append(menu, IdFullscreen, "Pause in Full-Screen Apps", s.PauseInFullscreen);
        Append(menu, IdSoftware, "Low-Memory Rendering (CPU)", s.SoftwareRendering);
        Append(menu, IdStartup, "Start with Windows", s.StartWithWindows);
        Separator(menu);
        Append(menu, IdResetHigh, s.HighScore > 0 ? $"Reset High Score ({s.HighScore})" : "Reset High Score", enabled: s.HighScore > 0);
        Append(menu, IdOpenSettings, "Open Settings File");
        Append(menu, IdResetSettings, "Reset Settings");
        Separator(menu);
        Append(menu, IdExit, "Exit");

        System.Drawing.Point cursor;
        _ = PInvoke.GetCursorPos(&cursor);

        // A popup menu only takes keyboard input and dismisses on an outside click if its owner is the
        // foreground window. Our click was swallowed by a hook, so Windows won't grant foreground to us
        // directly; attach to the current foreground thread's input queue for the duration of the menu,
        // then hand focus straight back to the app the user was using.
        HWND previous = PInvoke.GetForegroundWindow();
        uint previousThread = previous.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(previous, null);
        uint ownThread = PInvoke.GetCurrentThreadId();
        bool attached = previousThread != 0 && previousThread != ownThread && PInvoke.AttachThreadInput(previousThread, ownThread, true);
        _ = PInvoke.SetForegroundWindow(_hwnd);
        BOOL result = PInvoke.TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd | TpmBottomAlign, cursor.X, cursor.Y, _hwnd, null);
        _ = PInvoke.PostMessage(_hwnd, PInvoke.WM_NULL, default, default); // per MSDN: lets the menu close cleanly
        if (!previous.IsNull && PInvoke.IsWindow(previous))
        {
            _ = PInvoke.SetForegroundWindow(previous); // never leave the user's app without focus
        }

        if (attached)
        {
            _ = PInvoke.AttachThreadInput(previousThread, ownThread, false);
        }

        _ = PInvoke.DestroyMenu(personality);
        _ = PInvoke.DestroyMenu(sizes);
        _ = PInvoke.DestroyMenu(colors);
        _ = PInvoke.DestroyMenu(menu);

        Dispatch((uint)result.Value);
    }

    private void Dispatch(uint id)
    {
        switch (id)
        {
            case 0: return;
            case >= IdActivityBase: Command?.Invoke(TrayCommand.Activity, (int)(id - IdActivityBase)); return;
            case >= IdSizeBase: Command?.Invoke(TrayCommand.Size, (int)(id - IdSizeBase)); return;
            case >= IdThemeBase: Command?.Invoke(TrayCommand.Theme, (int)(id - IdThemeBase)); return;
        }

        TrayCommand? command = id switch
        {
            IdStartGame => TrayCommand.StartGame,
            IdHide1 => TrayCommand.HideOneHour,
            IdHide4 => TrayCommand.HideFourHours,
            IdShowNow => TrayCommand.ShowNow,
            IdFullscreen => TrayCommand.TogglePauseInFullscreen,
            IdSoftware => TrayCommand.ToggleSoftwareRendering,
            IdStartup => TrayCommand.ToggleStartWithWindows,
            IdResetHigh => TrayCommand.ResetHighScore,
            IdOpenSettings => TrayCommand.OpenSettingsFile,
            IdResetSettings => TrayCommand.ResetSettings,
            IdExit => TrayCommand.Exit,
            _ => null,
        };

        if (command is { } c)
        {
            Command?.Invoke(c, 0);
        }
    }

    private static void Append(HMENU menu, uint id, string text, bool isChecked = false, bool enabled = true)
    {
        MENU_ITEM_FLAGS flags = MENU_ITEM_FLAGS.MF_STRING | (isChecked ? MENU_ITEM_FLAGS.MF_CHECKED : 0) | (enabled ? 0 : MENU_ITEM_FLAGS.MF_GRAYED);
        fixed (char* p = text)
        {
            _ = PInvoke.AppendMenu(menu, flags, id, p);
        }
    }

    private static void Separator(HMENU menu) => PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);

    private static void Submenu(HMENU menu, HMENU submenu, string text)
    {
        fixed (char* p = text)
        {
            _ = PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_POPUP, (nuint)(nint)submenu.Value, p);
        }
    }

    private void ReplaceIcon(ReadOnlySpan<Bgra32> pixels, int size)
    {
        HICON old = _icon;
        _icon = CreateHIcon(pixels, size);
        if (!old.IsNull)
        {
            _ = PInvoke.DestroyIcon(old);
        }
    }

    private static HICON CreateHIcon(ReadOnlySpan<Bgra32> pixels, int size)
    {
        HBITMAP color;
        fixed (Bgra32* p = pixels)
        {
            color = PInvoke.CreateBitmap(size, size, 1, 32, p);
        }

        int maskStride = ((size + 15) / 16) * 2;
        Span<byte> maskBits = stackalloc byte[maskStride * size]; // all zero: colour alpha decides
        HBITMAP mask;
        fixed (byte* m = maskBits)
        {
            mask = PInvoke.CreateBitmap(size, size, 1, 1, m);
        }

        var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
        HICON icon = PInvoke.CreateIconIndirect(&info);

        _ = PInvoke.DeleteObject(color);
        _ = PInvoke.DeleteObject(mask);
        return icon;
    }
}
