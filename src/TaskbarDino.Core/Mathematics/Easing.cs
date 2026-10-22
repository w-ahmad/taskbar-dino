namespace TaskbarDino.Core.Mathematics;

/// <summary>A normalized easing function: maps a progress value in [0, 1] to an eased value.</summary>
public delegate float EaseFunc(float t);

/// <summary>
/// Standard easing curves for smooth motion. Every function takes and returns a value in [0, 1]
/// (inputs are clamped). Used by the movement controller and animation transitions so the dino
/// accelerates and settles naturally instead of moving linearly.
/// </summary>
public static class Easing
{
    public static float Linear(float t) => MathUtil.Clamp01(t);

    public static float SmoothStep(float t)
    {
        t = MathUtil.Clamp01(t);
        return t * t * (3f - (2f * t));
    }

    public static float EaseInQuad(float t)
    {
        t = MathUtil.Clamp01(t);
        return t * t;
    }

    public static float EaseOutQuad(float t)
    {
        t = MathUtil.Clamp01(t);
        return 1f - ((1f - t) * (1f - t));
    }

    public static float EaseInOutQuad(float t)
    {
        t = MathUtil.Clamp01(t);
        return t < 0.5f ? 2f * t * t : 1f - (MathF.Pow((-2f * t) + 2f, 2f) / 2f);
    }

    public static float EaseOutCubic(float t)
    {
        t = MathUtil.Clamp01(t);
        return 1f - MathF.Pow(1f - t, 3f);
    }

    /// <summary>Overshoots slightly past the target then settles — good for a springy landing/hop.</summary>
    public static float EaseOutBack(float t)
    {
        t = MathUtil.Clamp01(t);
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + (c3 * MathF.Pow(t - 1f, 3f)) + (c1 * MathF.Pow(t - 1f, 2f));
    }

    /// <summary>A decaying bounce — useful for a playful landing.</summary>
    public static float EaseOutBounce(float t)
    {
        t = MathUtil.Clamp01(t);
        const float n1 = 7.5625f;
        const float d1 = 2.75f;

        if (t < 1f / d1)
        {
            return n1 * t * t;
        }

        if (t < 2f / d1)
        {
            t -= 1.5f / d1;
            return (n1 * t * t) + 0.75f;
        }

        if (t < 2.5f / d1)
        {
            t -= 2.25f / d1;
            return (n1 * t * t) + 0.9375f;
        }

        t -= 2.625f / d1;
        return (n1 * t * t) + 0.984375f;
    }
}
