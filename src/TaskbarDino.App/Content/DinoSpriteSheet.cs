using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;
using TaskbarDino.Core.Settings;

namespace TaskbarDino.App.Content;

/// <summary>
/// The built-in, original pixel-art dino. One base body is authored as a character grid; eye and
/// legs are painted programmatically per frame into the atlas: idle, blink, glance-left,
/// glance-right, two walk frames and a jump. Colours come from a per-theme palette (the art is a
/// simple three-tone body + white eye), so switching theme just re-rasterises with a new palette.
/// Everything is generated at runtime — no third-party art, no image decoding.
/// </summary>
public static class DinoSpriteSheet
{
    public const int FrameWidth = 24;
    public const int FrameHeight = 24;
    public const int FrameCount = 7;
    public const int AtlasWidth = FrameWidth * FrameCount;
    public const int AtlasHeight = FrameHeight;

    private static readonly string[] Body =
    [
        "........................",
        ".............ooooooo",
        "............o#######o",
        "............o#######o",
        "............o#######o",
        "............o#######o",
        "............o#######o",
        "............o#######o",
        "...........o#######o",
        "......oo...o######o",
        ".....o##o..o#######o",
        "....o##ooooo########o",
        "...o################o",
        "..o#################o",
        "..o#################o",
        "..o################o",
        "..o###==========##o",
        "..o##============##o",
        "..o##############o",
    ];

    private static readonly string[] LegsStand =
    [
        "....##.....##.....",
        "....##.....##.....",
        "....##.....##.....",
        "...o##o...o##o....",
        "...####...####....",
    ];

    private static readonly string[] LegsWalkA =
    [
        "...##......##.....",
        "..##.......##.....",
        "..##........##....",
        ".o##o......o##o...",
        ".###........###...",
    ];

    private static readonly string[] LegsWalkB =
    [
        ".....##..##.......",
        ".....##..##.......",
        ".....##..##.......",
        "....o##..##o......",
        "....###..###......",
    ];

    private static readonly string[] LegsJump =
    [
        "....###..###......",
        ".....#....#.......",
        "..................",
        "..................",
        "..................",
    ];

    private static readonly Bgra32 EyeWhite = Bgra32.Rgb(0xF2, 0xF2, 0xF2);
    private static readonly Bgra32 Pupil = Bgra32.Rgb(0x1E, 0x1E, 0x1E);

    private static readonly Dictionary<DinoTheme, Palette> Palettes = new()
    {
        [DinoTheme.ChromeGray] = new(Bgra32.Rgb(0x3A, 0x3A, 0x3A), Bgra32.Rgb(0x53, 0x53, 0x53), Bgra32.Rgb(0x6E, 0x6E, 0x6E)),
        [DinoTheme.Green] = new(Bgra32.Rgb(0x2E, 0x6B, 0x2E), Bgra32.Rgb(0x49, 0x9E, 0x4C), Bgra32.Rgb(0x63, 0xC2, 0x66)),
        [DinoTheme.Blue] = new(Bgra32.Rgb(0x2E, 0x4E, 0x80), Bgra32.Rgb(0x44, 0x78, 0xC4), Bgra32.Rgb(0x66, 0x99, 0xE6)),
        [DinoTheme.Red] = new(Bgra32.Rgb(0x7E, 0x2E, 0x2E), Bgra32.Rgb(0xC2, 0x4A, 0x4A), Bgra32.Rgb(0xE0, 0x66, 0x66)),
        [DinoTheme.Purple] = new(Bgra32.Rgb(0x54, 0x3A, 0x7E), Bgra32.Rgb(0x86, 0x59, 0xBE), Bgra32.Rgb(0xA9, 0x82, 0xD6)),
        [DinoTheme.Gold] = new(Bgra32.Rgb(0x7E, 0x62, 0x20), Bgra32.Rgb(0xC2, 0x9A, 0x38), Bgra32.Rgb(0xE0, 0xBC, 0x54)),
    };

    private static readonly Rgba AccentFallback = Rgba.Rgb(0x2E, 0x78, 0xD4);

    /// <summary>The palette for a theme; the Accent theme derives a three-tone palette from the OS accent colour.</summary>
    private static Palette PaletteFor(DinoTheme theme, Rgba accent) =>
        theme == DinoTheme.Accent ? AccentPalette(accent.A == 0 ? AccentFallback : accent) : Palettes[theme];

    private static Palette AccentPalette(Rgba a)
    {
        Bgra32 body = Bgra32.Rgb(a.R, a.G, a.B);
        Bgra32 outline = Bgra32.Rgb(Scale(a.R, 0.5f), Scale(a.G, 0.5f), Scale(a.B, 0.5f));
        Bgra32 belly = Bgra32.Rgb(Lighten(a.R), Lighten(a.G), Lighten(a.B));
        return new Palette(outline, body, belly);
    }

    private static byte Scale(byte c, float factor) => (byte)Math.Clamp(c * factor, 0f, 255f);

    private static byte Lighten(byte c) => (byte)Math.Clamp(c + ((255 - c) * 0.35f), 0f, 255f);

    private const int LegsY = 19;
    private const int EyeX = 14;
    private const int EyeY = 4;
    private const int EyeW = 4;
    private const int EyeH = 3;

    private enum Eye
    {
        Open,
        Closed,
        Left,
        Right,
    }

    private readonly record struct Palette(Bgra32 Outline, Bgra32 Body, Bgra32 Belly);

    private static readonly (string Name, Eye Eye, string[] Legs)[] Frames =
    [
        ("idle", Eye.Open, LegsStand),
        ("blink", Eye.Closed, LegsStand),
        ("look_left", Eye.Left, LegsStand),
        ("look_right", Eye.Right, LegsStand),
        ("walk1", Eye.Open, LegsWalkA),
        ("walk2", Eye.Open, LegsWalkB),
        ("jump", Eye.Open, LegsJump),
    ];

    public static IReadOnlyDictionary<string, AtlasRegion> Regions { get; } = BuildRegions();

    /// <summary>Rasterises the whole atlas for a theme into a premultiplied BGRA buffer for GPU upload.</summary>
    public static Bgra32[] CreatePixels(DinoTheme theme, Rgba accent = default)
    {
        Palette palette = PaletteFor(theme, accent);
        var atlas = new Bgra32[AtlasWidth * AtlasHeight];
        for (int i = 0; i < Frames.Length; i++)
        {
            int offsetX = i * FrameWidth;
            PaintGrid(atlas, AtlasWidth, offsetX, Body, 0, palette);
            PaintGrid(atlas, AtlasWidth, offsetX, Frames[i].Legs, LegsY, palette);
            PaintEye(atlas, AtlasWidth, offsetX, Frames[i].Eye);
        }

        return atlas;
    }

    /// <summary>Renders just the idle pose, scaled to <paramref name="size"/>², for a tray icon.</summary>
    public static Bgra32[] CreateIconPixels(DinoTheme theme, int size, Rgba accent = default)
    {
        Palette palette = PaletteFor(theme, accent);
        var frame = new Bgra32[FrameWidth * FrameHeight];
        PaintGrid(frame, FrameWidth, 0, Body, 0, palette);
        PaintGrid(frame, FrameWidth, 0, LegsStand, LegsY, palette);
        PaintEye(frame, FrameWidth, 0, Eye.Open);

        var icon = new Bgra32[size * size];
        for (int y = 0; y < size; y++)
        {
            int sy = y * FrameHeight / size;
            for (int x = 0; x < size; x++)
            {
                int sx = x * FrameWidth / size;
                icon[(y * size) + x] = frame[(sy * FrameWidth) + sx];
            }
        }

        return icon;
    }

    private static Dictionary<string, AtlasRegion> BuildRegions()
    {
        var regions = new Dictionary<string, AtlasRegion>(Frames.Length);
        for (int i = 0; i < Frames.Length; i++)
        {
            regions[Frames[i].Name] = new AtlasRegion(new PixelRect(i * FrameWidth, 0, FrameWidth, FrameHeight));
        }

        return regions;
    }

    private static void PaintGrid(Bgra32[] buffer, int stride, int offsetX, string[] rows, int yStart, Palette palette)
    {
        for (int r = 0; r < rows.Length; r++)
        {
            int y = yStart + r;
            if (y >= FrameHeight)
            {
                break;
            }

            string row = rows[r];
            for (int x = 0; x < FrameWidth; x++)
            {
                char c = x < row.Length ? row[x] : '.';
                Bgra32? color = c switch
                {
                    'o' => palette.Outline,
                    '#' => palette.Body,
                    '=' => palette.Belly,
                    _ => null,
                };

                if (color is { } value)
                {
                    buffer[(y * stride) + offsetX + x] = value;
                }
            }
        }
    }

    private static void PaintEye(Bgra32[] buffer, int stride, int offsetX, Eye eye)
    {
        if (eye == Eye.Closed)
        {
            for (int x = EyeX; x < EyeX + EyeW; x++)
            {
                buffer[((EyeY + 1) * stride) + offsetX + x] = Pupil;
            }

            return;
        }

        for (int y = EyeY; y < EyeY + EyeH; y++)
        {
            for (int x = EyeX; x < EyeX + EyeW; x++)
            {
                buffer[(y * stride) + offsetX + x] = EyeWhite;
            }
        }

        int pupilX = eye switch
        {
            Eye.Left => EyeX,
            Eye.Right => EyeX + 2,
            _ => EyeX + 1,
        };

        buffer[((EyeY + 1) * stride) + offsetX + pupilX] = Pupil;
        buffer[((EyeY + 1) * stride) + offsetX + pupilX + 1] = Pupil;
    }
}
