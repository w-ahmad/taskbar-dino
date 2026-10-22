namespace TaskbarDino.Core.Settings;

/// <summary>User-facing, persisted settings. A small immutable record saved as JSON.</summary>
public sealed record AppSettings
{
    public DinoTheme Theme { get; init; } = DinoTheme.Accent;

    public DinoSize Size { get; init; } = DinoSize.Small;

    public bool StartWithWindows { get; init; }

    /// <summary>Best score reached in Game Mode, kept between runs.</summary>
    public int HighScore { get; init; }
}

/// <summary>Loads, exposes and persists <see cref="AppSettings"/>, notifying on change.</summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>The on-disk location of the settings file (for the "Settings" menu command).</summary>
    string Location { get; }

    /// <summary>Applies a change to the settings, persists it, and raises <see cref="Changed"/>.</summary>
    void Update(Func<AppSettings, AppSettings> mutate);

    event EventHandler<AppSettings>? Changed;
}
