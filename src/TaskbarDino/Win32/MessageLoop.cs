using System.Diagnostics;

using TaskbarDino.Core.Time;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Win32;

/// <summary>
/// The Win32 message pump and frame scheduler. After each frame the app says what it needs next:
/// <see cref="FrameOutcome.Idle"/> blocks in <c>WaitMessage</c> (0% CPU) until an OS message or a
/// <see cref="Wake"/> arrives; <see cref="FrameOutcome.WakeIn"/> waits at most that long; and
/// <see cref="FrameOutcome.Continuous"/> paces to the 60 FPS cap. Every wait ends early on input.
/// </summary>
internal sealed unsafe class MessageLoop
{
    public const double TargetFps = 60.0;
    private const double FrameBudgetMs = 1000.0 / TargetFps;

    private readonly long _start = Stopwatch.GetTimestamp();
    private uint _threadId;
    private volatile bool _quit;

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_start);

    public void Run(Func<FrameTime, FrameOutcome> onFrame)
    {
        _threadId = PInvoke.GetCurrentThreadId();
        TimeSpan last = Elapsed;

        while (!_quit)
        {
            if (!DrainMessages())
            {
                break;
            }

            TimeSpan now = Elapsed;
            FrameOutcome outcome = onFrame(new FrameTime(now, now - last));
            last = now;

            if (_quit)
            {
                break;
            }

            if (outcome.SleepFor is not { } sleepFor)
            {
                _ = PInvoke.WaitMessage(); // nothing scheduled: block until a message arrives
            }
            else if (sleepFor <= TimeSpan.Zero)
            {
                WaitForFrameBudget(now);   // animating: pace to the frame budget
            }
            else
            {
                Wait(sleepFor.TotalMilliseconds); // resting until the next scheduled change
            }
        }
    }

    /// <summary>Breaks the loop out of an idle wait so it processes another frame (thread-safe).</summary>
    public void Wake()
    {
        if (_threadId != 0)
        {
            _ = PInvoke.PostThreadMessage(_threadId, PInvoke.WM_NULL, default, default);
        }
    }

    public void Quit()
    {
        _quit = true;
        Wake();
    }

    /// <summary>Processes every queued message. Returns <see langword="false"/> on <c>WM_QUIT</c>.</summary>
    private bool DrainMessages()
    {
        MSG msg;
        while (PInvoke.PeekMessage(&msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
        {
            if (msg.message == PInvoke.WM_QUIT)
            {
                _quit = true;
                return false;
            }

            _ = PInvoke.TranslateMessage(&msg);
            _ = PInvoke.DispatchMessage(&msg);
        }

        return true;
    }

    private void WaitForFrameBudget(TimeSpan frameStart)
    {
        double remainingMs = FrameBudgetMs - (Elapsed - frameStart).TotalMilliseconds;
        if (remainingMs >= 1d)
        {
            Wait(remainingMs);
        }
    }

    private static void Wait(double milliseconds)
    {
        uint ms = (uint)Math.Clamp(milliseconds, 1d, 3_600_000d);
        _ = PInvoke.MsgWaitForMultipleObjectsEx(0, null, ms, QUEUE_STATUS_FLAGS.QS_ALLINPUT, MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);
    }
}
