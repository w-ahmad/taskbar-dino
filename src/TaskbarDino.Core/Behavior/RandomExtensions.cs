namespace TaskbarDino.Core.Behavior;

internal static class RandomExtensions
{
    /// <summary>A uniformly distributed float in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    public static float NextRange(this Random rng, float min, float max) => min + ((float)rng.NextDouble() * (max - min));
}
