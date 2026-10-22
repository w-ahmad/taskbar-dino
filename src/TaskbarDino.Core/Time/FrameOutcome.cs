namespace TaskbarDino.Core.Time;

/// <summary>
/// What a frame callback asks the loop to do next. This is the knob that keeps idle CPU near zero:
/// the dino spends most of its life resting on a single static frame, so it returns
/// <see cref="WakeIn"/> with the time until its next spontaneous behaviour and the loop sleeps until
/// then; only while something is actually animating does it return <see cref="Continuous"/>.
/// </summary>
public readonly record struct FrameOutcome
{
    private FrameOutcome(TimeSpan? sleepFor) => SleepFor = sleepFor;

    /// <summary>
    /// <see langword="null"/> = sleep until an OS event or wake; <see cref="TimeSpan.Zero"/> = render
    /// again next frame at the target rate; positive = sleep at most this long (waking early on input).
    /// </summary>
    public TimeSpan? SleepFor { get; }

    /// <summary>Keep rendering at the target frame rate — something is animating.</summary>
    public static FrameOutcome Continuous => new(TimeSpan.Zero);

    /// <summary>Nothing is happening; block until an event arrives.</summary>
    public static FrameOutcome Idle => new((TimeSpan?)null);

    /// <summary>Rest until <paramref name="delay"/> elapses (or an event arrives), then tick again.</summary>
    public static FrameOutcome WakeIn(TimeSpan delay) => new(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
}
