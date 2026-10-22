namespace TaskbarDino.Core.Mathematics;

/// <summary>Small, allocation-free numeric helpers used across animation, movement and physics.</summary>
public static class MathUtil
{
    /// <summary>Linearly interpolates from <paramref name="a"/> to <paramref name="b"/> by <paramref name="t"/>.</summary>
    public static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    /// <summary>Returns the fraction of the way <paramref name="value"/> lies between <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static float InverseLerp(float a, float b, float value) =>
        MathF.Abs(b - a) < 1e-6f ? 0f : (value - a) / (b - a);

    /// <summary>Clamps <paramref name="value"/> to the inclusive range [0, 1].</summary>
    public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

    /// <summary>Approximate float equality using an absolute epsilon.</summary>
    public static bool Approximately(float a, float b, float epsilon = 1e-4f) => MathF.Abs(a - b) <= epsilon;

    /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by at most <paramref name="maxDelta"/>.</summary>
    public static float MoveTowards(float current, float target, float maxDelta)
    {
        float diff = target - current;
        if (MathF.Abs(diff) <= maxDelta)
        {
            return target;
        }

        return current + (MathF.Sign(diff) * maxDelta);
    }
}
