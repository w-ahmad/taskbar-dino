using System.Runtime.InteropServices;

namespace TaskbarDino.Core.Graphics;

/// <summary>
/// A 32-bit premultiplied pixel laid out as B, G, R, A in memory — byte-for-byte the DXGI
/// <c>B8G8R8A8_UNORM</c> format of the swap chain, so pixel buffers upload to the GPU unconverted.
/// The art is fully opaque or fully transparent, so premultiplied and straight alpha coincide.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Bgra32(byte B, byte G, byte R, byte A)
{
    public static Bgra32 Transparent => default;

    /// <summary>Creates an opaque pixel from red/green/blue components.</summary>
    public static Bgra32 Rgb(byte r, byte g, byte b) => new(b, g, r, 255);

    public bool IsOpaque => A != 0;
}

/// <summary>An opaque RGB colour (the Windows accent colour, palette entries).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public Bgra32 ToBgra32() => Bgra32.Rgb(R, G, B);

    /// <summary>Multiplies each channel by <paramref name="factor"/> (darkens below 1).</summary>
    public Rgb Scale(float factor) => new(Mul(R, factor), Mul(G, factor), Mul(B, factor));

    /// <summary>Moves each channel toward white by <paramref name="amount"/> in [0, 1].</summary>
    public Rgb Lighten(float amount) => new(Toward(R, amount), Toward(G, amount), Toward(B, amount));

    private static byte Mul(byte c, float f) => (byte)Math.Clamp(MathF.Round(c * f), 0f, 255f);

    private static byte Toward(byte c, float t) => (byte)Math.Clamp(MathF.Round(c + ((255 - c) * t)), 0f, 255f);
}
