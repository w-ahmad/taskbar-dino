using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Win32;

/// <summary>Identifies the current foreground window and the process behind it.</summary>
internal sealed record ActiveWindow(HWND Handle, uint ProcessId, string ProcessName, string Title);

/// <summary>
/// Tracks the foreground window via an out-of-context <c>EVENT_SYSTEM_FOREGROUND</c> hook — fully
/// event-driven, nothing polls while things are steady. Shell UI (task switcher, Start, search), tool
/// windows and our own windows are ignored so the dino stays put during those; the desktop
/// (Win+D) reports <see langword="null"/>.
/// </summary>
/// <remarks>
/// A just-launched app becomes foreground a beat before it finishes showing, so its foreground event
/// arrives while the window is still invisible and never repeats. An unready foreground window arms
/// a short, bounded timer that re-checks until the window appears, then follows it. The timer only
/// exists during a launch, so idle CPU is unaffected.
/// </remarks>
internal sealed unsafe class ForegroundTracker : IDisposable
{
    private const uint ReadyPollIntervalMs = 250;
    private const int ReadyPollMaxAttempts = 16;

    private static readonly string[] IgnoredClasses =
    [
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "XamlExplorerHostIslandWindow",   // Windows 11 Alt+Tab / Win+Tab task view
        "MultitaskingViewFrame",          // older task view
        "ForegroundStaging",
        "Windows.UI.Core.CoreWindow",     // Start, Search, Action Center, emoji picker
        "TaskSwitcherWnd",                // classic Alt+Tab
        "DV2ControlHost",
        "Shell_CharmWindow",
        "WorkerW",                        // wallpaper host; gets foreground briefly during shell transitions
        "SearchHost",                     // Windows 11 search flyout
        OverlayWindow.ClassName,
    ];

    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    private static ForegroundTracker? _instance;

    private HWINEVENTHOOK _hook;
    private nuint _readyPollTimer;
    private int _readyPollAttempts;

    public ActiveWindow? Current { get; private set; }

    /// <summary>Raised on the UI thread when the followed window changes (<see langword="null"/> = desktop).</summary>
    public event Action<ActiveWindow?>? Changed;

    public void Start()
    {
        _instance = this;
        _hook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, HMODULE.Null, &OnWinEvent, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);

        HWND foreground = PInvoke.GetForegroundWindow();
        Current = Classify(foreground) == Kind.App ? Capture(foreground) : null;
    }

    /// <summary>Re-reads the foreground (after resume / Explorer restart) and raises <see cref="Changed"/> if it differs.</summary>
    public void Refresh()
    {
        HWND foreground = PInvoke.GetForegroundWindow();
        Kind kind = Classify(foreground);
        if (kind == Kind.App)
        {
            ActiveWindow? info = Capture(foreground);
            if (info is not null && info.Handle != Current?.Handle)
            {
                Commit(info);
            }
        }
        else if (kind == Kind.Desktop && Current is not null)
        {
            Current = null;
            Changed?.Invoke(null);
        }
    }

    public void Dispose()
    {
        StopReadyPoll();
        if (!_hook.IsNull)
        {
            _ = PInvoke.UnhookWinEvent(_hook);
            _hook = default;
        }

        _instance = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnWinEvent(HWINEVENTHOOK hook, uint eventType, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (idObject != 0 || idChild != 0 || _instance is not { } self) // OBJID_WINDOW only
        {
            return;
        }

        try
        {
            self.OnForeground(hwnd);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "foreground event");
        }
    }

    private void OnForeground(HWND hwnd)
    {
        switch (Classify(hwnd))
        {
            case Kind.App:
                StopReadyPoll();
                ActiveWindow? info = Capture(hwnd);
                if (info is not null)
                {
                    Commit(info);
                }

                break;

            case Kind.NotReady:
                StartReadyPoll();
                break;

            case Kind.Desktop:
                StopReadyPoll();
                if (Current is not null)
                {
                    Current = null;
                    Log.Info($"Foreground → desktop");
                    Changed?.Invoke(null);
                }

                break;

            case Kind.Ignorable:
                break; // a poll already in flight keeps running: a launch may still be settling
        }
    }

    private void Commit(ActiveWindow info)
    {
        Current = info;
        Log.Info($"Foreground → '{info.ProcessName}' \"{info.Title}\"");
        Changed?.Invoke(info);
    }

    private void StartReadyPoll()
    {
        _readyPollAttempts = 0;
        if (_readyPollTimer == 0)
        {
            _readyPollTimer = PInvoke.SetTimer(HWND.Null, 0, ReadyPollIntervalMs, &OnReadyPollTick);
        }
    }

    private void StopReadyPoll()
    {
        if (_readyPollTimer != 0)
        {
            _ = PInvoke.KillTimer(HWND.Null, _readyPollTimer);
            _readyPollTimer = 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnReadyPollTick(HWND hwnd, uint message, nuint timerId, uint time)
    {
        if (_instance is not { } self)
        {
            return;
        }

        HWND foreground = PInvoke.GetForegroundWindow();
        if (Classify(foreground) == Kind.App)
        {
            ActiveWindow? info = Capture(foreground);
            if (info is not null && self.Current?.Handle != info.Handle)
            {
                self.Commit(info);
            }

            self.StopReadyPoll();
            return;
        }

        if (++self._readyPollAttempts >= ReadyPollMaxAttempts)
        {
            self.StopReadyPoll();
        }
    }

    private enum Kind
    {
        App,
        Ignorable,
        Desktop,
        NotReady,
    }

    private static Kind Classify(HWND hwnd)
    {
        if (hwnd.IsNull)
        {
            return Kind.Ignorable;
        }

        uint processId;
        _ = PInvoke.GetWindowThreadProcessId(hwnd, &processId);
        if (processId == OwnProcessId)
        {
            return Kind.Ignorable;
        }

        var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if ((exStyle & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0)
        {
            return Kind.Ignorable;
        }

        Span<char> buffer = stackalloc char[64];
        ReadOnlySpan<char> cls = buffer[..WindowText.ClassName(hwnd, buffer)];
        if (cls.SequenceEqual("Progman"))
        {
            return Kind.Desktop;
        }

        foreach (string ignored in IgnoredClasses)
        {
            if (cls.SequenceEqual(ignored))
            {
                return Kind.Ignorable;
            }
        }

        return PInvoke.IsWindowVisible(hwnd) ? Kind.App : Kind.NotReady;
    }

    private static ActiveWindow? Capture(HWND hwnd)
    {
        if (hwnd.IsNull || !PInvoke.IsWindowVisible(hwnd))
        {
            return null;
        }

        uint processId;
        _ = PInvoke.GetWindowThreadProcessId(hwnd, &processId);
        return new ActiveWindow(hwnd, processId, ProcessName(processId), WindowText.Title(hwnd));
    }

    /// <summary>The executable's base name without extension (empty if the process can't be opened).</summary>
    private static string ProcessName(uint processId)
    {
        HANDLE process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsNull)
        {
            return string.Empty;
        }

        try
        {
            Span<char> buffer = stackalloc char[512];
            uint length = (uint)buffer.Length;
            fixed (char* p = buffer)
            {
                if (!PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, p, &length))
                {
                    return string.Empty;
                }
            }

            ReadOnlySpan<char> path = buffer[..(int)length];
            int slash = path.LastIndexOf('\\');
            ReadOnlySpan<char> file = slash >= 0 ? path[(slash + 1)..] : path;
            int dot = file.LastIndexOf('.');
            return new string(dot > 0 ? file[..dot] : file);
        }
        finally
        {
            _ = PInvoke.CloseHandle(process);
        }
    }
}
