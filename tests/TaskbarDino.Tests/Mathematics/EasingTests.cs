using TaskbarDino.Core.Mathematics;

namespace TaskbarDino.Tests.Mathematics;

public class EasingTests
{
    public static TheoryData<EaseFunc> NormalizedCurves => new()
    {
        Easing.Linear,
        Easing.SmoothStep,
        Easing.EaseInQuad,
        Easing.EaseOutQuad,
        Easing.EaseInOutQuad,
        Easing.EaseOutCubic,
        Easing.EaseOutBounce,
    };

    [Theory]
    [MemberData(nameof(NormalizedCurves))]
    public void Curve_StartsAtZero_And_EndsAtOne(EaseFunc ease)
    {
        Assert.Equal(0f, ease(0f), precision: 3);
        Assert.Equal(1f, ease(1f), precision: 3);
    }

    [Theory]
    [MemberData(nameof(NormalizedCurves))]
    public void Curve_ClampsOutOfRangeInput(EaseFunc ease)
    {
        Assert.Equal(0f, ease(-5f), precision: 3);
        Assert.Equal(1f, ease(5f), precision: 3);
    }

    [Fact]
    public void EaseOutBack_OvershootsPastOne_BeforeSettling()
    {
        // The springy curve should exceed 1.0 somewhere in the second half of its travel.
        bool overshot = false;
        for (float t = 0.5f; t < 1f; t += 0.01f)
        {
            if (Easing.EaseOutBack(t) > 1f)
            {
                overshot = true;
                break;
            }
        }

        Assert.True(overshot);
    }
}
