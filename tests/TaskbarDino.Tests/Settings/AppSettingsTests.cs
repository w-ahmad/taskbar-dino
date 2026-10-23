using TaskbarDino.Core.Settings;

namespace TaskbarDino.Tests.Settings;

public class AppSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Default_HasSensibleValues()
    {
        var s = new AppSettings();
        Assert.Equal(DinoTheme.Accent, s.Theme);
        Assert.Equal(DinoSize.Medium, s.Size);
        Assert.True(s.PauseInFullscreen);
        Assert.False(s.StartWithWindows);
        Assert.Null(s.HiddenUntil);
        Assert.False(s.IsHiddenAt(Now));
    }

    [Fact]
    public void IsHiddenAt_WallClock_ExpiresOnTime()
    {
        AppSettings s = new AppSettings() with { HiddenUntil = Now.AddHours(1) };

        Assert.True(s.IsHiddenAt(Now));
        Assert.Equal(TimeSpan.FromMinutes(30), s.HiddenRemaining(Now.AddMinutes(30)));
        Assert.False(s.IsHiddenAt(Now.AddHours(1)));
        Assert.Equal(TimeSpan.Zero, s.HiddenRemaining(Now.AddHours(2)));
    }

    [Fact]
    public void IsHiddenAt_AbsurdFutureValue_IsIgnored()
    {
        AppSettings s = new AppSettings() with { HiddenUntil = Now.AddDays(30) };
        Assert.False(s.IsHiddenAt(Now));
    }

    [Fact]
    public void IsHiddenAt_AfterLongSleep_HasExpired()
    {
        AppSettings s = new AppSettings() with { HiddenUntil = Now.AddHours(4) };
        // The machine slept for five hours: the hide has simply elapsed.
        Assert.False(s.IsHiddenAt(Now.AddHours(5)));
    }
}
