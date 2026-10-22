using TaskbarDino.Core.Dino;

namespace TaskbarDino.Core.Settings;

/// <summary>User-facing, persisted settings. A small immutable record saved as JSON.</summary>
public sealed record AppSettings
{
    public DinoTheme Theme { get; init; } = DinoTheme.Accent;

    public DinoSize Size { get; init; } = DinoSize.Small;

    public ActivityLevel Activity { get; init; } = ActivityLevel.Normal;

    public bool StartWithWindows { get; init; }

    /// <summary>Hide the dino (and suspend all work) while a full-screen app is in the foreground.</summary>
    public bool PauseInFullscreen { get; init; } = true;

    /// <summary>Best score reached in Game Mode, kept between runs.</summary>
    public int HighScore { get; init; }

    /// <summary>
    /// Wall-clock instant (UTC) until which the dino stays hidden ("Hide for N hours"). Stored as an
    /// absolute time so the duration survives restarts and sleep/resume. <see langword="null"/> = visible.
    /// </summary>
    public DateTimeOffset? HiddenUntil { get; init; }

    /// <summary>Whether the dino is hidden at <paramref name="now"/>. A far-future value (clock change) is ignored.</summary>
    public bool IsHiddenAt(DateTimeOffset now) =>
        HiddenUntil is { } until && until > now && until - now <= MaxHideDuration;

    /// <summary>Time left on the hide, clamped to zero.</summary>
    public TimeSpan HiddenRemaining(DateTimeOffset now) =>
        IsHiddenAt(now) ? HiddenUntil!.Value - now : TimeSpan.Zero;

    /// <summary>The longest hide the menu offers; anything beyond it is treated as a clock glitch.</summary>
    public static TimeSpan MaxHideDuration => TimeSpan.FromHours(4);
}
