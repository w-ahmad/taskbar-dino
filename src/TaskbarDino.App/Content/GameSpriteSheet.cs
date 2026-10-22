using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;

namespace TaskbarDino.App.Content;

/// <summary>
/// Original pixel art for Game Mode's obstacles and score cloud, generated at runtime (the dino itself
/// is drawn from the themed companion atlas). The stage is transparent — it floats over the taskbar —
/// so every sprite gets an automatically generated 1-pixel outline in the contrasting ink, keeping it
/// legible on both light and dark taskbars: obstacles are a dark body with a light halo; the score
/// cloud is a light body with a dark edge; digits are dark with a light halo.
/// </summary>
public static class GameSpriteSheet
{
    public const int Stride = 160; // atlas width in pixels

    public const string CactusSmall = "cactus_small";
    public const string CactusLarge = "cactus_large";
    public const string Ptero1 = "ptero1";
    public const string Ptero2 = "ptero2";
    public const string Cloud = "cloud";
    public const string GlyphPrefix = "glyph_";

    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;

    private static readonly Bgra32 DarkInk = Bgra32.Rgb(0x2E, 0x2E, 0x2E);
    private static readonly Bgra32 LightInk = Bgra32.Rgb(0xF4, 0xF4, 0xF4);

    private enum Style
    {
        /// <summary>Dark fill, light outline — obstacles and digits (readable on any background).</summary>
        DarkBody,

        /// <summary>Light fill, dark outline — the score cloud.</summary>
        LightBody,
    }

    private sealed record Sprite(string Name, int Width, int Height, string[] Rows, Style Style);

    private static readonly Sprite[] Sprites;
    private static readonly Dictionary<string, AtlasRegion> RegionMap;
    private static readonly int AtlasHeightValue;

    static GameSpriteSheet()
    {
        Sprites = BuildSprites();
        RegionMap = Pack(Sprites, out AtlasHeightValue);
    }

    public static int AtlasWidth => Stride;

    public static int AtlasHeight => AtlasHeightValue;

    public static IReadOnlyDictionary<string, AtlasRegion> Regions => RegionMap;

    public static string Glyph(char c) => GlyphPrefix + c;

    /// <summary>Rasterises the atlas into a premultiplied BGRA buffer (bodies, then generated outlines).</summary>
    public static Bgra32[] CreatePixels()
    {
        var atlas = new Bgra32[Stride * AtlasHeightValue];

        foreach (Sprite sprite in Sprites)
        {
            PixelRect r = RegionMap[sprite.Name].Source;
            (Bgra32 body, Bgra32 outline) = sprite.Style == Style.LightBody ? (LightInk, DarkInk) : (DarkInk, LightInk);

            var isBody = new bool[sprite.Width, sprite.Height];
            for (int y = 0; y < sprite.Height; y++)
            {
                string row = y < sprite.Rows.Length ? sprite.Rows[y] : string.Empty;
                for (int x = 0; x < sprite.Width; x++)
                {
                    if (x < row.Length && row[x] == '#')
                    {
                        isBody[x, y] = true;
                        atlas[((r.Y + y) * Stride) + r.X + x] = body;
                    }
                }
            }

            // 1-pixel outline: any transparent cell touching a body cell (kept inside the sprite).
            for (int y = 0; y < sprite.Height; y++)
            {
                for (int x = 0; x < sprite.Width; x++)
                {
                    if (!isBody[x, y] && TouchesBody(isBody, x, y, sprite.Width, sprite.Height))
                    {
                        atlas[((r.Y + y) * Stride) + r.X + x] = outline;
                    }
                }
            }
        }

        return atlas;
    }

    private static bool TouchesBody(bool[,] body, int x, int y, int w, int h) =>
        (x > 0 && body[x - 1, y]) ||
        (x < w - 1 && body[x + 1, y]) ||
        (y > 0 && body[x, y - 1]) ||
        (y < h - 1 && body[x, y + 1]);

    private static Dictionary<string, AtlasRegion> Pack(Sprite[] sprites, out int atlasHeight)
    {
        var regions = new Dictionary<string, AtlasRegion>(sprites.Length);
        int x = 0;
        int y = 0;
        int shelfHeight = 0;

        foreach (Sprite sprite in sprites)
        {
            if (x + sprite.Width > Stride)
            {
                x = 0;
                y += shelfHeight + 2; // +2 so outlines never bleed between shelves
                shelfHeight = 0;
            }

            regions[sprite.Name] = new AtlasRegion(new PixelRect(x, y, sprite.Width, sprite.Height), default);
            x += sprite.Width + 2;
            shelfHeight = Math.Max(shelfHeight, sprite.Height);
        }

        atlasHeight = y + shelfHeight;
        return regions;
    }

    private static Sprite[] BuildSprites()
    {
        var list = new List<Sprite>
        {
            new(CactusSmall, 12, 18, CactusSmallGrid, Style.DarkBody),
            new(CactusLarge, 16, 24, CactusLargeGrid, Style.DarkBody),
            new(Ptero1, 24, 16, PteroGrid(true), Style.DarkBody),
            new(Ptero2, 24, 16, PteroGrid(false), Style.DarkBody),
            new(Cloud, 30, 13, CloudGrid, Style.LightBody),
        };

        foreach ((char c, string[] glyph) in Digits)
        {
            list.Add(new Sprite(GlyphPrefix + c, GlyphWidth, GlyphHeight, glyph, Style.DarkBody));
        }

        return [.. list];
    }

    private static readonly string[] CactusSmallGrid =
    [
        "....##......",
        ".#..##......",
        ".#..##......",
        ".#..##..#...",
        ".#..##..#...",
        ".####.#.#...",
        "..#.###.#...",
        "..#..####...",
        ".....##.....",
        ".....##.....",
        ".....##.....",
        ".....##.....",
        ".....##.....",
        ".....##.....",
        ".....##.....",
        ".....##.....",
        ".....##.....",
        ".....##.....",
    ];

    private static readonly string[] CactusLargeGrid =
    [
        "......##........",
        "..#...##...#....",
        "..#...##...#....",
        "..#...##...#....",
        "..#...##...#....",
        "..#.#.##.#.#....",
        "..###.##.###....",
        "...#..##..#.....",
        ".....####.......",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
        "......##........",
    ];

    private static string[] PteroGrid(bool wingsUp) => wingsUp
        ?
        [
            "....##..................",
            "...####.................",
            "..######................",
            ".#######................",
            "..#########.............",
            "...##########.......###.",
            "....###############.###.",
            "...................####.",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
        ]
        :
        [
            "........................",
            "........................",
            "........................",
            "...................####.",
            "....###############.###.",
            "...##########.......###.",
            "..#########.............",
            ".#######................",
            "..######................",
            "...####.................",
            "....##..................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
        ];

    // A rounded, filled bubble that holds the score, floating above the dino.
    private static readonly string[] CloudGrid =
    [
        "......################......",
        "....####################....",
        "..########################..",
        ".##########################.",
        ".##########################.",
        ".##########################.",
        ".##########################.",
        ".##########################.",
        ".##########################.",
        ".##########################.",
        "..########################..",
        "....####################....",
        "......################......",
    ];

    private static readonly Dictionary<char, string[]> Digits = new()
    {
        ['0'] = ["#####", "#...#", "#..##", "#.#.#", "##..#", "#...#", "#####"],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", "#####"],
        ['2'] = ["#####", "....#", "....#", "#####", "#....", "#....", "#####"],
        ['3'] = ["#####", "....#", "....#", ".####", "....#", "....#", "#####"],
        ['4'] = ["#...#", "#...#", "#...#", "#####", "....#", "....#", "....#"],
        ['5'] = ["#####", "#....", "#....", "#####", "....#", "....#", "#####"],
        ['6'] = ["#####", "#....", "#....", "#####", "#...#", "#...#", "#####"],
        ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        ['8'] = ["#####", "#...#", "#...#", "#####", "#...#", "#...#", "#####"],
        ['9'] = ["#####", "#...#", "#...#", "#####", "....#", "....#", "#####"],
    };
}
