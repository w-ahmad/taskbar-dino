using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Hosting;
using TaskbarDino.Core.Time;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskbarDino.Windows.Hosting;

/// <summary>
/// The Win32 message pump. Implements the low-idle-CPU frame model: after each frame the loop asks
/// the app whether anything is animating. If not (<see cref="FrameDemand.Sleep"/>) it blocks in
/// <c>WaitMessage</c> — genuinely 0% CPU — until an OS message or a <see cref="Wake"/> arrives. While
/// animating (<see cref="FrameDemand.Continue"/>) it paces to ~60 FPS but still wakes early on input.
/// </summary>
public sealed unsafe class Win32MessageLoop(IClock clock, ILogger<Win32MessageLoop> logger) : IMessageLoop
{
    private const double TargetFps = 60.0;
    private const double FrameBudgetMs = 1000.0 / TargetFps;

    private readonly IClock _clock = clock;
    private readonly ILogger<Win32MessageLoop> _logger = logger;

    private uint _threadId;
    private volatile bool _quit;

    public void Run(Func<FrameTime, FrameOutcome> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);

        _threadId = PInvoke.GetCurrentThreadId();
        long frameIndex = 0;
        TimeSpan last = _clock.Elapsed;

        while (!_quit)
        {
            if (!DrainMessages())
            {
                break;
            }

            TimeSpan now = _clock.Elapsed;
            var time = new FrameTime(now, now - last, frameIndex++);
            last = now;

            FrameOutcome outcome = onFrame(time);

            if (outcome.SleepFor is not { } sleepFor)
            {
                // Nothing scheduled: block until a message arrives. 0% CPU.
                _ = PInvoke.WaitMessage();
            }
            else if (sleepFor <= TimeSpan.Zero)
            {
                // Actively animating: pace to the frame budget, waking early on input.
                WaitForFrameBudget(now);
            }
            else
            {
                // Resting until the next scheduled behaviour, but wake early on input.
                double ms = Math.Clamp(sleepFor.TotalMilliseconds, 1d, 3_600_000d);
                _ = PInvoke.MsgWaitForMultipleObjectsEx(
                    0,
                    null,
                    (uint)ms,
                    QUEUE_STATUS_FLAGS.QS_ALLINPUT,
                    MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);
            }
        }

        _logger.LogInformation("Message loop exited.");
    }

    public void Wake()
    {
        // Break WaitMessage / MsgWaitForMultipleObjectsEx on the loop thread with a benign message.
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
        while (PInvoke.PeekMessage(out MSG msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
        {
            if (msg.message == PInvoke.WM_QUIT)
            {
                _quit = true;
                return false;
            }

            _ = PInvoke.TranslateMessage(in msg);
            _ = PInvoke.DispatchMessage(in msg);
        }

        return true;
    }

    /// <summary>Waits out the remainder of the frame budget, waking early if input arrives.</summary>
    private void WaitForFrameBudget(TimeSpan frameStart)
    {
        double elapsedMs = (_clock.Elapsed - frameStart).TotalMilliseconds;
        int remainingMs = (int)(FrameBudgetMs - elapsedMs);
        if (remainingMs <= 0)
        {
            return;
        }

        _ = PInvoke.MsgWaitForMultipleObjectsEx(
            0,
            null,
            (uint)remainingMs,
            QUEUE_STATUS_FLAGS.QS_ALLINPUT,
            MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);
    }
}
