using TaskbarDino.Core.Mathematics;

namespace TaskbarDino.Tests.Mathematics;

public class MathUtilTests
{
    [Theory]
    [InlineData(0f, 10f, 0f, 0f)]
    [InlineData(0f, 10f, 1f, 10f)]
    [InlineData(0f, 10f, 0.5f, 5f)]
    [InlineData(-10f, 10f, 0.5f, 0f)]
    public void Lerp_InterpolatesLinearly(float a, float b, float t, float expected) =>
        Assert.Equal(expected, MathUtil.Lerp(a, b, t), precision: 4);

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    public void Clamp01_ClampsToUnitRange(float value, float expected) =>
        Assert.Equal(expected, MathUtil.Clamp01(value), precision: 4);

    [Fact]
    public void InverseLerp_IsInverseOfLerp()
    {
        float v = MathUtil.Lerp(20f, 60f, 0.25f);
        Assert.Equal(0.25f, MathUtil.InverseLerp(20f, 60f, v), precision: 4);
    }

    [Fact]
    public void InverseLerp_ReturnsZero_WhenRangeIsDegenerate() =>
        Assert.Equal(0f, MathUtil.InverseLerp(5f, 5f, 5f), precision: 4);

    [Theory]
    [InlineData(0f, 10f, 3f, 3f)]
    [InlineData(9f, 10f, 3f, 10f)]   // clamps at the target
    [InlineData(10f, 0f, 3f, 7f)]    // moves downward
    public void MoveTowards_StepsTowardTargetWithoutOvershoot(float current, float target, float maxDelta, float expected) =>
        Assert.Equal(expected, MathUtil.MoveTowards(current, target, maxDelta), precision: 4);
}
