namespace TaskbarDino.Core.Time;

/// <summary>Timing information handed to every <see cref="ITickable"/> once per frame.</summary>
/// <param name="Total">Monotonic time since the loop started.</param>
/// <param name="Delta">Time since the previous frame.</param>
/// <param name="FrameIndex">Monotonically increasing frame counter.</param>
public readonly record struct FrameTime(TimeSpan Total, TimeSpan Delta, long FrameIndex)
{
    /// <summary>Delta time in seconds — the value physics and animation integrate against.</summary>
    public float DeltaSeconds => (float)Delta.TotalSeconds;

    public double TotalSeconds => Total.TotalSeconds;
}

/// <summary>
/// What a component wants the frame loop to do after it ticks. This is the heart of the
/// low-idle-CPU design: when everything returns <see cref="Sleep"/> the loop stops spinning and
/// blocks on the message queue (0% CPU) until an event wakes it.
/// </summary>
public enum FrameDemand
{
    /// <summary>Nothing is moving; it is safe for the loop to go idle and block until an event arrives.</summary>
    Sleep = 0,

    /// <summary>Something is animating; schedule another frame at the target frame rate.</summary>
    Continue = 1,
}

/// <summary>A component that advances by a time step each frame.</summary>
public interface ITickable
{
    /// <summary>Advances state by <paramref name="time"/> and reports whether it needs more frames.</summary>
    FrameDemand Tick(in FrameTime time);
}
