namespace TaskbarDino.Core.Time;

/// <summary>Timing information handed to the frame callback once per frame.</summary>
/// <param name="Total">Monotonic time since the loop started.</param>
/// <param name="Delta">Time since the previous frame.</param>
public readonly record struct FrameTime(TimeSpan Total, TimeSpan Delta)
{
    /// <summary>Delta time in seconds — the value physics and animation integrate against.</summary>
    public float DeltaSeconds => (float)Delta.TotalSeconds;
}
