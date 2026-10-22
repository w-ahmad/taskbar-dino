using TaskbarDino.Core.Time;

namespace TaskbarDino.Core.Hosting;

/// <summary>
/// The application's run loop. On Windows this owns the Win32 message pump on the UI thread.
/// It also fulfils <see cref="IFrameScheduler"/>: <see cref="IFrameScheduler.Wake"/> breaks the
/// loop out of an idle wait so it processes another frame.
/// </summary>
public interface IMessageLoop : IFrameScheduler
{
    /// <summary>
    /// Runs until <see cref="Quit"/> (or the OS ends the session). Each iteration drains pending OS
    /// messages, then calls <paramref name="onFrame"/> and honours the returned <see cref="FrameOutcome"/>:
    /// sleep indefinitely, sleep for a bounded time, or pace to the target frame rate — always waking
    /// early if input arrives.
    /// </summary>
    void Run(Func<FrameTime, FrameOutcome> onFrame);

    /// <summary>Requests the loop stop and return from <see cref="Run"/>.</summary>
    void Quit();
}
