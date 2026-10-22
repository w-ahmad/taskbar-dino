using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Win32;

/// <summary>
/// Detects a full-screen application in the foreground (games, videos, presentations) so the dino
/// can get out of the way. Uses the documented notification-state API that Windows itself uses to
/// suppress toasts, plus a geometric check of the foreground window against its monitor (so an
/// F11 browser or a borderless game on a secondary display also counts).
/// </summary>
internal static unsafe class Fullscreen
{
    public static bool IsActive(out MonitorInfo? monitor)
    {
        monitor = null;

        QUERY_USER_NOTIFICATION_STATE state;
        if (PInvoke.SHQueryUserNotificationState(&state).Succeeded &&
            state is QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY or QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
        {
            HWND fg = PInvoke.GetForegroundWindow();
            monitor = fg.IsNull ? Monitors.Primary : Monitors.FromWindow(fg);
            return true;
        }

        HWND foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull || PInvoke.IsIconic(foreground))
        {
            return false;
        }

        Span<char> buffer = stackalloc char[64];
        ReadOnlySpan<char> cls = buffer[..WindowText.ClassName(foreground, buffer)];
        if (cls.SequenceEqual("Progman") || cls.SequenceEqual("WorkerW") || TaskbarWindows.IsTaskbarClass(cls))
        {
            return false; // the desktop itself covers the monitor but is not "full screen"
        }

        RECT rect;
        if (!PInvoke.GetWindowRect(foreground, &rect))
        {
            return false;
        }

        MonitorInfo m = Monitors.FromWindow(foreground);
        var window = PixelRect.FromLTRB(rect.left, rect.top, rect.right, rect.bottom);
        if (window.Covers(m.Bounds))
        {
            monitor = m;
            return true;
        }

        return false;
    }
}
