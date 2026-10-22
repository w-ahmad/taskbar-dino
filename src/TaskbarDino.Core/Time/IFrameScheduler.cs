namespace TaskbarDino.Core.Time;

/// <summary>
/// The bridge between asynchronous event sources (foreground changes, input, tray commands) and
/// the frame loop. When the loop is idle/blocked, calling <see cref="Wake"/> guarantees at least
/// one more frame is processed so the dino can react. Implementations must be thread-safe: wakes
/// may originate from OS callbacks on other threads.
/// </summary>
public interface IFrameScheduler
{
    /// <summary>Requests that the loop process at least one more frame, breaking out of an idle wait.</summary>
    void Wake();
}
