using System.Numerics;
using System.Runtime.InteropServices;

namespace TaskbarDino.Core.Graphics;

/// <summary>
/// A 32-bit pixel laid out as B, G, R, A in memory — byte-for-byte compatible with the DXGI
/// <c>B8G8R8A8_UNORM</c> format used by the Direct2D/DirectComposition swap chain, so pixel buffers
/// can be uploaded to the GPU without conversion.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Bgra32(byte B, byte G, byte R, byte A)
{
    public static Bgra32 Transparent => default;

    /// <summary>Creates an opaque pixel from red/green/blue components.</summary>
    public static Bgra32 Rgb(byte r, byte g, byte b) => new(b, g, r, 255);
}

/// <summary>
/// A straight-alpha RGBA colour used for theming/tinting and clear colours. Kept as bytes for
/// compactness; converts to the premultiplied <see cref="Bgra32"/> the GPU expects.
/// </summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    public static Rgba White => new(255, 255, 255, 255);
    public static Rgba Black => new(0, 0, 0, 255);
    public static Rgba Transparent => default;

    /// <summary>Creates a colour from a packed <c>0xAARRGGBB</c> value.</summary>
    public static Rgba FromArgb(uint argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    /// <summary>Creates an opaque colour from red, green and blue.</summary>
    public static Rgba Rgb(byte r, byte g, byte b) => new(r, g, b, 255);

    /// <summary>Normalized straight-alpha components in [0, 1], as Direct2D's <c>D2D1_COLOR_F</c> expects.</summary>
    public Vector4 ToVector4() => new(R / 255f, G / 255f, B / 255f, A / 255f);

    /// <summary>Converts to a premultiplied BGRA pixel for GPU upload.</summary>
    public Bgra32 ToPremultipliedBgra32()
    {
        float a = A / 255f;
        return new Bgra32((byte)(B * a), (byte)(G * a), (byte)(R * a), A);
    }
}
