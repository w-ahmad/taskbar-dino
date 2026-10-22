using System.Diagnostics;

namespace TaskbarDino.Core.Time;

/// <summary>A monotonic, high-resolution time source. Abstracted so tests can supply a fake clock.</summary>
public interface IClock
{
    /// <summary>Monotonic time elapsed since the clock was created. Never goes backwards.</summary>
    TimeSpan Elapsed { get; }
}

/// <summary>
/// The production clock, backed by <see cref="Stopwatch"/> (QueryPerformanceCounter under the hood),
/// giving sub-millisecond resolution for smooth frame pacing.
/// </summary>
public sealed class SystemClock : IClock
{
    private readonly long _start = Stopwatch.GetTimestamp();

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_start);
}
