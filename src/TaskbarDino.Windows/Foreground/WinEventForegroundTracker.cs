using System.Diagnostics;

using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Platform;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Windows.Foreground;

/// <summary>
/// Tracks the foreground window via a <c>WINEVENT_OUTOFCONTEXT</c> hook on
/// <c>EVENT_SYSTEM_FOREGROUND</c> — fully event-driven, so nothing polls while things are steady. The
/// callback is delivered on this (UI) thread while the message loop pumps, so raising the event here is
/// thread-safe.
/// </summary>
/// <remarks>
/// A newly launched app frequently becomes the foreground window a beat <em>before</em> it finishes
/// showing, so its <c>EVENT_SYSTEM_FOREGROUND</c> arrives while the window is still invisible. Because
/// the app then stays foreground, no further foreground event ever fires — and clicking the already-
/// foreground window raises nothing either — so the dino would never follow it until the user switched
/// apps and back. To handle that, an unready foreground window arms a short, bounded thread timer that
/// re-checks the foreground until the window appears, then follows it. The timer only exists during a
/// launch and stops the instant the window is ready, so idle CPU is unaffected.
/// </remarks>
public sealed unsafe class WinEventForegroundTracker : IActiveWindowTracker
{
    private const int ObjIdWindow = 0; // OBJID_WINDOW

    // Readiness poll for a just-launched foreground window: re-check every 250 ms, up to ~4 s, which
    // comfortably covers the "appears within a second or two" launch the user observed.
    private const uint ReadyPollIntervalMs = 250;
    private const int ReadyPollMaxAttempts = 16;

    // Foreground changes to these are ignored, so the dino stays put during Alt+Tab / Win+Tab, the
    // Start menu, Search, showing the desktop, etc.
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "XamlExplorerHostIslandWindow",   // Windows 11 Alt+Tab switcher / Win+Tab task view
        "MultitaskingViewFrame",          // older task view
        "ForegroundStaging",
        "Windows.UI.Core.CoreWindow",     // Start, Search, Action Center, emoji picker
        "TaskSwitcherWnd",                // classic Alt+Tab
        "DV2ControlHost",
        "Shell_CharmWindow",
        // WorkerW is the desktop-icon/wallpaper host and gets foreground briefly during many shell
        // transitions (Start menu open/close, Action Center, etc.).  Treating it as Desktop would
        // send the dino to the Start button during normal shell use; Progman alone is sufficient to
        // detect the real "Show Desktop" action (Win+D or the Show-Desktop strip).
        "WorkerW",
        "SearchHost",                     // Windows 11 search flyout
    };

    // The desktop itself (Show Desktop / Win+D). Foreground landing here sends the dino to the Start button.
    // NOTE: WorkerW is intentionally NOT here — it fires during Start-menu and other shell transitions.
    private static readonly HashSet<string> DesktopClasses = new(StringComparer.Ordinal)
    {
        "Progman",
    };

    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    private readonly ILogger<WinEventForegroundTracker> _logger;
    private readonly WINEVENTPROC _callback;  // held so the native thunk is not collected
    private readonly TIMERPROC _readyPoll;     // held so the native thunk is not collected

    private HWINEVENTHOOK _hook;
    private nuint _readyPollTimer;
    private int _readyPollAttempts;

    public WinEventForegroundTracker(ILogger<WinEventForegroundTracker> logger)
    {
        _logger = logger;
        _callback = OnWinEvent;
        _readyPoll = OnReadyPollTick;
    }

    public ActiveWindowInfo? Current { get; private set; }

    public event EventHandler<ActiveWindowInfo?>? ForegroundChanged;

    public void Start()
    {
        _hook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            HINSTANCE.Null,
            _callback,
            idProcess: 0,
            idThread: 0,
            PInvoke.WINEVENT_OUTOFCONTEXT);

        HWND foreground = PInvoke.GetForegroundWindow();
        Current = Classify(foreground) == WindowKind.App ? Capture(foreground) : null;
        _logger.LogInformation("Foreground tracker started (current: {App}).", Current?.ProcessName ?? "<none>");
    }

    public void Dispose()
    {
        StopReadyPoll();

        if (_hook.Value is not null)
        {
            _ = PInvoke.UnhookWinEvent(_hook);
            _hook = default;
        }
    }

    private void OnWinEvent(HWINEVENTHOOK hook, uint eventType, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        // Only care about the window itself becoming foreground, not child objects.
        if (idObject != ObjIdWindow || idChild != 0)
        {
            return;
        }

        switch (Classify(hwnd))
        {
            case WindowKind.App:
                // A real application took focus — follow it now and cancel any pending readiness poll.
                StopReadyPoll();
                Accept(hwnd);
                break;

            case WindowKind.NotReady:
                // A just-launched app grabbed focus before it finished showing. It won't raise another
                // foreground event, so poll until it is visible, then follow it.
                StartReadyPoll();
                break;

            case WindowKind.Desktop:
                // Show Desktop / Win+D: there's no active app, so send the dino to the Start button.
                StopReadyPoll();
                if (Current is not null)
                {
                    Current = null;
                    _logger.LogDebug("Foreground → desktop; sending dino to Start.");
                    ForegroundChanged?.Invoke(this, null);
                }

                break;

            case WindowKind.Ignorable:
                // Task switcher, Start, our own overlay, a tool window, … — leave the dino where it is.
                // A poll already in flight keeps running: a launch may still be settling behind this.
                break;
        }
    }

    /// <summary>Captures <paramref name="hwnd"/> and, if it is a real window, follows it.</summary>
    private void Accept(HWND hwnd)
    {
        ActiveWindowInfo? info = Capture(hwnd);
        if (info is not null)
        {
            Commit(info);
        }
    }

    private void Commit(ActiveWindowInfo info)
    {
        Current = info;
        _logger.LogDebug("Foreground → '{Proc}'.", info.ProcessName);
        ForegroundChanged?.Invoke(this, info);
    }

    private void StartReadyPoll()
    {
        _readyPollAttempts = 0;
        _readyPollTimer = _readyPollTimer != 0
            ? _readyPollTimer
            : PInvoke.SetTimer(HWND.Null, 0, ReadyPollIntervalMs, _readyPoll);
    }

    private void StopReadyPoll()
    {
        if (_readyPollTimer != 0)
        {
            _ = PInvoke.KillTimer(HWND.Null, _readyPollTimer);
            _readyPollTimer = 0;
        }
    }

    /// <summary>
    /// Fires on the UI thread (dispatched by the message loop) while a launching window is still
    /// showing. Once the foreground resolves to a ready application, follow it and stop polling.
    /// </summary>
    private void OnReadyPollTick(HWND hwnd, uint message, nuint timerId, uint time)
    {
        HWND foreground = PInvoke.GetForegroundWindow();
        if (Classify(foreground) == WindowKind.App)
        {
            ActiveWindowInfo? info = Capture(foreground);
            if (info is not null && Current?.Handle != info.Handle)
            {
                Commit(info);
            }

            StopReadyPoll();
            return;
        }

        if (++_readyPollAttempts >= ReadyPollMaxAttempts)
        {
            StopReadyPoll();
        }
    }

    private enum WindowKind
    {
        /// <summary>A normal, visible application window — the dino should follow it.</summary>
        App,

        /// <summary>Shell UI, the task switcher, tool windows, or our own overlay — never followed.</summary>
        Ignorable,

        /// <summary>The desktop is showing (Show Desktop / Win+D) — send the dino to the Start button.</summary>
        Desktop,

        /// <summary>A real app window that has taken focus but not finished showing yet.</summary>
        NotReady,
    }

    private static WindowKind Classify(HWND hwnd)
    {
        if (hwnd.IsNull)
        {
            return WindowKind.Ignorable;
        }

        uint processId = 0;
        _ = PInvoke.GetWindowThreadProcessId(hwnd, &processId);
        if (processId == OwnProcessId)
        {
            return WindowKind.Ignorable;
        }

        var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if ((exStyle & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0)
        {
            return WindowKind.Ignorable;
        }

        string cls = ReadClassName(hwnd);
        if (DesktopClasses.Contains(cls))
        {
            return WindowKind.Desktop;
        }

        if (IgnoredClasses.Contains(cls))
        {
            return WindowKind.Ignorable;
        }

        // A real application window — but a freshly launched one may not have finished showing yet.
        return PInvoke.IsWindowVisible(hwnd) ? WindowKind.App : WindowKind.NotReady;
    }

    private static string ReadClassName(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[128];
        int length;
        fixed (char* p = buffer)
        {
            length = PInvoke.GetClassName(hwnd, new PWSTR(p), buffer.Length);
        }

        return length > 0 ? new string(buffer[..length]) : string.Empty;
    }

    private static ActiveWindowInfo? Capture(HWND hwnd)
    {
        if (hwnd.IsNull || !PInvoke.IsWindowVisible(hwnd))
        {
            return null;
        }

        uint processId = 0;
        _ = PInvoke.GetWindowThreadProcessId(hwnd, &processId);

        string title = ReadTitle(hwnd);
        string processName = ReadProcessName((int)processId);

        return new ActiveWindowInfo(hwnd, (int)processId, processName, title);
    }

    private static string ReadTitle(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        int length;
        fixed (char* p = buffer)
        {
            length = PInvoke.GetWindowText(hwnd, new PWSTR(p), buffer.Length);
        }

        return length > 0 ? new string(buffer[..length]) : string.Empty;
    }

    private static string ReadProcessName(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
