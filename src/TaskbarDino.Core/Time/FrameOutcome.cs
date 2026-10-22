namespace TaskbarDino.Core.Time;

/// <summary>
/// What a frame asks the loop to do next. This is the knob that keeps idle CPU at zero: the dino
/// spends most of its life on a static frame and returns <see cref="WakeIn"/> with the time until
/// its next scheduled change (or <see cref="Idle"/> when only an external event can change anything),
/// and the loop blocks until then. Only while something is genuinely moving does it return
/// <see cref="Continuous"/>, which paces to the 60 FPS cap.
/// </summary>
public readonly record struct FrameOutcome
{
    private FrameOutcome(TimeSpan? sleepFor) => SleepFor = sleepFor;

    /// <summary>
    /// <see langword="null"/> = block until an OS event or wake; <see cref="TimeSpan.Zero"/> = render
    /// again at the capped frame rate; positive = sleep at most this long (waking early on events).
    /// </summary>
    public TimeSpan? SleepFor { get; }

    /// <summary>Keep rendering at the capped frame rate — something is animating.</summary>
    public static FrameOutcome Continuous => new(TimeSpan.Zero);

    /// <summary>Nothing scheduled; block until an event arrives.</summary>
    public static FrameOutcome Idle => new((TimeSpan?)null);

    /// <summary>Rest until <paramref name="delay"/> elapses (or an event arrives), then tick again.</summary>
    public static FrameOutcome WakeIn(TimeSpan delay) => new(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);

    /// <summary>The sooner of two outcomes (continuous beats everything; idle yields to any deadline).</summary>
    public static FrameOutcome Min(FrameOutcome a, FrameOutcome b)
    {
        if (a.SleepFor is null) return b;
        if (b.SleepFor is null) return a;
        return a.SleepFor.Value <= b.SleepFor.Value ? a : b;
    }
}
