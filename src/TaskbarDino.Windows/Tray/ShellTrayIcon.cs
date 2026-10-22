using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Platform;
using TaskbarDino.Core.Settings;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Windows.Tray;

/// <summary>
/// System-tray presence: a notification-area icon (built from the dino's own pixels) plus a
/// right-click context menu. Owns a hidden window purely to receive the tray callback and host the
/// menu, and re-adds itself if Explorer restarts (the <c>TaskbarCreated</c> broadcast).
/// </summary>
public sealed unsafe class ShellTrayIcon : ITrayIcon
{
    private const uint TrayCallbackMessage = PInvoke.WM_APP + 1;
    private const uint TrayIconId = 1;

    private const uint IdStartGame = 1;
    private const uint IdHide1 = 2;
    private const uint IdHide4 = 3;
    private const uint IdStartup = 4;
    private const uint IdSettings = 5;
    private const uint IdExit = 6;
    private const uint IdColorBase = 100;
    private const uint IdSizeBase = 200;

    // TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN
    private const uint MenuFlags = 0x0002 | 0x0100 | 0x0020;

    private static readonly (DinoTheme Theme, string Label)[] ThemeItems =
    [
        (DinoTheme.Accent, "Accent (Windows)"),
        (DinoTheme.ChromeGray, "Chrome Gray"),
        (DinoTheme.Green, "Green"),
        (DinoTheme.Blue, "Blue"),
        (DinoTheme.Red, "Red"),
        (DinoTheme.Purple, "Purple"),
        (DinoTheme.Gold, "Gold"),
    ];

    private static readonly (DinoSize Size, string Label)[] SizeItems =
    [
        (DinoSize.Tiny, "Tiny"),
        (DinoSize.Small, "Small"),
        (DinoSize.Medium, "Medium"),
        (DinoSize.Large, "Large"),
    ];

    private static int _classCounter;

    private readonly ILogger<ShellTrayIcon> _logger;
    private readonly string _className = $"TaskbarDinoTray_{Interlocked.Increment(ref _classCounter)}";
    private readonly WNDPROC _wndProc;
    private readonly HINSTANCE _hInstance;
    private readonly uint _taskbarCreatedMessage;

    private HWND _hwnd;
    private HICON _icon;
    private string _tooltip = "Taskbar Dino";
    private bool _installed;
    private bool _startupChecked;
    private DinoTheme _activeTheme = DinoTheme.ChromeGray;
    private DinoSize _activeSize = DinoSize.Medium;

    public ShellTrayIcon(ILogger<ShellTrayIcon> logger)
    {
        _logger = logger;
        _wndProc = WndProc;
        _hInstance = new HINSTANCE(Marshal.GetHINSTANCE(typeof(ShellTrayIcon).Module));

        fixed (char* taskbarCreated = "TaskbarCreated")
        {
            _taskbarCreatedMessage = PInvoke.RegisterWindowMessage(taskbarCreated);
        }

        CreateOwnerWindow();
    }

    public event EventHandler<TrayCommand>? CommandInvoked;

    public event EventHandler<DinoTheme>? ThemeSelected;

    public event EventHandler<DinoSize>? SizeSelected;

    public void Install(ReadOnlySpan<Bgra32> iconPixels, int width, int height, string tooltip)
    {
        _tooltip = tooltip;
        _icon = CreateHIcon(iconPixels, width, height);
        AddOrUpdateIcon(NOTIFY_ICON_MESSAGE.NIM_ADD);
        _installed = true;
        _logger.LogInformation("Tray icon installed.");
    }

    public void UpdateIcon(ReadOnlySpan<Bgra32> iconPixels, int width, int height)
    {
        HICON old = _icon;
        _icon = CreateHIcon(iconPixels, width, height);
        if (_installed)
        {
            AddOrUpdateIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY);
        }

        if (!old.IsNull)
        {
            _ = PInvoke.DestroyIcon(old);
        }
    }

    public void ShowMenu() => ShowContextMenu();

    public void SetStartupChecked(bool isChecked) => _startupChecked = isChecked;

    public void SetActiveTheme(DinoTheme theme) => _activeTheme = theme;

    public void SetActiveSize(DinoSize size) => _activeSize = size;

    public void Dispose()
    {
        if (_installed)
        {
            var data = new NOTIFYICONDATAW { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = _hwnd, uID = TrayIconId };
            _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
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
            fixed (char* classNamePtr = _className)
            {
                _ = PInvoke.UnregisterClass(new PCWSTR(classNamePtr), _hInstance);
            }
        }
    }

    private void CreateOwnerWindow()
    {
        fixed (char* classNamePtr = _className)
        {
            var wndClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _wndProc,
                hInstance = _hInstance,
                lpszClassName = new PCWSTR(classNamePtr),
            };

            if (PInvoke.RegisterClassEx(in wndClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register tray window class.");
            }

            _hwnd = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, new PCWSTR(classNamePtr), default, WINDOW_STYLE.WS_POPUP, 0, 0, 0, 0, HWND.Null, HMENU.Null, _hInstance, null);
        }

        if (_hwnd.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create tray owner window.");
        }
    }

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == TrayCallbackMessage)
        {
            uint mouseEvent = (uint)(lParam.Value & 0xFFFF);
            if (mouseEvent is PInvoke.WM_RBUTTONUP or PInvoke.WM_LBUTTONUP)
            {
                ShowContextMenu();
            }

            return new LRESULT(0);
        }

        if (msg == _taskbarCreatedMessage && _installed)
        {
            AddOrUpdateIcon(NOTIFY_ICON_MESSAGE.NIM_ADD);
            return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void AddOrUpdateIcon(NOTIFY_ICON_MESSAGE message)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon,
        };
        _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127)).CopyTo(data.szTip.AsSpan());

        if (!PInvoke.Shell_NotifyIcon(message, in data))
        {
            _logger.LogWarning("Shell_NotifyIcon({Message}) failed.", message);
        }
    }

    private void ShowContextMenu()
    {
        HMENU menu = PInvoke.CreatePopupMenu();
        HMENU colors = PInvoke.CreatePopupMenu();
        HMENU sizes = PInvoke.CreatePopupMenu();

        foreach ((DinoTheme theme, string label) in ThemeItems)
        {
            var flags = MENU_ITEM_FLAGS.MF_STRING;
            if (theme == _activeTheme)
            {
                flags |= MENU_ITEM_FLAGS.MF_CHECKED;
            }

            AppendString(colors, flags, IdColorBase + (uint)theme, label);
        }

        foreach ((DinoSize size, string label) in SizeItems)
        {
            var flags = MENU_ITEM_FLAGS.MF_STRING;
            if (size == _activeSize)
            {
                flags |= MENU_ITEM_FLAGS.MF_CHECKED;
            }

            AppendString(sizes, flags, IdSizeBase + (uint)size, label);
        }

        AppendString(menu, MENU_ITEM_FLAGS.MF_STRING, IdStartGame, "Start Game");
        AppendSeparator(menu);
        AppendString(menu, MENU_ITEM_FLAGS.MF_STRING, IdHide1, "Hide for 1 Hour");
        AppendString(menu, MENU_ITEM_FLAGS.MF_STRING, IdHide4, "Hide for 4 Hours");
        AppendSubmenu(menu, colors, "Change Dino Color");
        AppendSubmenu(menu, sizes, "Dino Size");
        AppendString(menu, MENU_ITEM_FLAGS.MF_STRING | (_startupChecked ? MENU_ITEM_FLAGS.MF_CHECKED : MENU_ITEM_FLAGS.MF_UNCHECKED), IdStartup, "Start with Windows");
        AppendSeparator(menu);
        AppendString(menu, MENU_ITEM_FLAGS.MF_STRING, IdSettings, "Settings");
        AppendString(menu, MENU_ITEM_FLAGS.MF_STRING, IdExit, "Exit");

        _ = PInvoke.GetCursorPos(out System.Drawing.Point cursor);
        _ = PInvoke.SetForegroundWindow(_hwnd); // so the menu dismisses on outside click

        var result = PInvoke.TrackPopupMenuEx(menu, MenuFlags, cursor.X, cursor.Y, _hwnd, null);

        _ = PInvoke.DestroyMenu(sizes);
        _ = PInvoke.DestroyMenu(colors);
        _ = PInvoke.DestroyMenu(menu);

        Dispatch((uint)result.Value);
    }

    private void Dispatch(uint commandId)
    {
        if (commandId == 0)
        {
            return;
        }

        if (commandId >= IdSizeBase)
        {
            SizeSelected?.Invoke(this, (DinoSize)(commandId - IdSizeBase));
            return;
        }

        if (commandId >= IdColorBase)
        {
            ThemeSelected?.Invoke(this, (DinoTheme)(commandId - IdColorBase));
            return;
        }

        TrayCommand? command = commandId switch
        {
            IdStartGame => TrayCommand.StartGame,
            IdHide1 => TrayCommand.HideOneHour,
            IdHide4 => TrayCommand.HideFourHours,
            IdStartup => TrayCommand.ToggleStartWithWindows,
            IdSettings => TrayCommand.OpenSettings,
            IdExit => TrayCommand.Exit,
            _ => null,
        };

        if (command is { } value)
        {
            CommandInvoked?.Invoke(this, value);
        }
    }

    private void AppendString(HMENU menu, MENU_ITEM_FLAGS flags, uint id, string text)
    {
        fixed (char* p = text)
        {
            _ = PInvoke.AppendMenu(menu, flags, id, new PCWSTR(p));
        }
    }

    private static void AppendSeparator(HMENU menu) => PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, default);

    private void AppendSubmenu(HMENU menu, HMENU submenu, string text)
    {
        fixed (char* p = text)
        {
            _ = PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_POPUP, (nuint)submenu.Value, new PCWSTR(p));
        }
    }

    private HICON CreateHIcon(ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        HBITMAP color;
        fixed (Bgra32* p = pixels)
        {
            color = PInvoke.CreateBitmap(width, height, 1, 32, p);
        }

        int maskStride = ((width + 15) / 16) * 2;
        var maskBits = new byte[maskStride * height];
        HBITMAP mask;
        fixed (byte* m = maskBits)
        {
            mask = PInvoke.CreateBitmap(width, height, 1, 1, m);
        }

        var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
        DestroyIconSafeHandle handle = PInvoke.CreateIconIndirect(in info);

        _ = PInvoke.DeleteObject((HGDIOBJ)(nint)color.Value);
        _ = PInvoke.DeleteObject((HGDIOBJ)(nint)mask.Value);

        if (handle.IsInvalid)
        {
            _logger.LogWarning("Custom tray icon creation failed; using the stock application icon.");
            return PInvoke.LoadIcon(HINSTANCE.Null, PInvoke.IDI_APPLICATION);
        }

        HICON icon = new(handle.DangerousGetHandle());
        handle.SetHandleAsInvalid(); // take ownership of the HICON; we DestroyIcon it ourselves
        return icon;
    }
}
