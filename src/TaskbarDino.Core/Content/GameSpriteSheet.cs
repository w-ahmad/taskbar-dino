using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Content;

/// <summary>
/// Pixel art for Game Mode at the dino's pixel density (the dino itself comes from the themed
/// companion sheet): obstacles and decor from <see cref="GameArt"/>, the score bubble, a 5×7 font
/// and the restart glyph. The stage is transparent — it floats over the taskbar — so every sprite
/// gets an automatically generated one-pixel outline in the contrasting ink, keeping it legible on
/// light and dark taskbars alike.
/// </summary>
public static class GameSpriteSheet
{
    public const int AtlasWidth = 256;

    public const string CactusSmall = "cactus_small";
    public const string CactusLarge = "cactus_large";
    public const string Ptero1 = "ptero1";
    public const string Ptero2 = "ptero2";
    public const string Bubble = "bubble";
    public const string Cloud = "cloud";
    public const string Ground = "ground";
    public const string Restart = "restart";
    public const string GlyphPrefix = "glyph_";

    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;
    public const int BubbleWidth = 88;
    public const int BubbleHeight = 42;
    public const int GroundWidth = 96; // tiles seamlessly: no horizontal outline padding

    private static readonly Bgra32 DarkInk = Bgra32.Rgb(0x2E, 0x2E, 0x2E);
    private static readonly Bgra32 LightInk = Bgra32.Rgb(0xF4, 0xF4, 0xF4);
    private static readonly Bgra32 CloudInk = Bgra32.Rgb(0xDE, 0xDE, 0xDE);

    private readonly record struct Sprite(string Name, string[] Rows, bool LightBody, bool Tile = false);

    private static readonly Sprite[] Sprites; // built in the static ctor, after the grids below are initialised

    public static IReadOnlyDictionary<string, AtlasRegion> Regions { get; }

    public static int AtlasHeight { get; }

    static GameSpriteSheet()
    {
        Sprites = BuildSprites();
        var cells = new (string, int, int)[Sprites.Length];
        for (int i = 0; i < Sprites.Length; i++)
        {
            Sprite s = Sprites[i];
            cells[i] = (s.Name, s.Rows[0].Length + (s.Tile ? 0 : 2), s.Rows.Length + 2);
        }

        Regions = PixelArt.Pack(cells, AtlasWidth, 0, out int height);
        AtlasHeight = height;
    }

    public static string Glyph(char c) => GlyphPrefix + char.ToUpperInvariant(c);

    /// <summary>Rasterises the atlas into a premultiplied BGRA sheet (bodies, then generated outlines).</summary>
    public static SpriteSheet Create()
    {
        var pixels = new Bgra32[AtlasWidth * AtlasHeight];
        var dark = new PixelArt.Ink(new[] { ('#', DarkInk) });
        var light = new PixelArt.Ink(new[] { ('#', LightInk) });
        var cloud = new PixelArt.Ink(new[] { ('#', CloudInk) });

        foreach (Sprite sprite in Sprites)
        {
            PixelRect cell = Regions[sprite.Name].Source;
            PixelArt.Paint(pixels, AtlasWidth, cell, sprite.Tile ? 0 : 1, 1, sprite.Rows, sprite.Name == Cloud ? cloud : sprite.LightBody ? light : dark);
            PixelArt.Outline(pixels, AtlasWidth, cell, sprite.LightBody ? DarkInk : LightInk);
        }

        return new SpriteSheet(AtlasWidth, AtlasHeight, pixels, Regions);
    }

    private static Sprite[] BuildSprites()
    {
        var list = new List<Sprite>
        {
            new(CactusSmall, GameArt.CactusSmall, false),
            new(CactusLarge, GameArt.CactusLarge, false),
            new(Ptero1, GameArt.Ptero1, false),
            new(Ptero2, GameArt.Ptero2, false),
            new(Bubble, BuildBubble(), true),
            new(Cloud, GameArt.Cloud, true),
            new(Ground, GameArt.Ground, false, Tile: true),
            new(Restart, RestartGrid, false),
        };

        foreach ((char c, string[] glyph) in Font)
        {
            list.Add(new Sprite(GlyphPrefix + c, glyph, false));
        }

        return [.. list];
    }

    // A rounded speech-bubble that holds "HI 0123" over the current score, floating above the dino.
    private static string[] BuildBubble()
    {
        const int w = BubbleWidth - 2, h = BubbleHeight - 2;
        var rows = new string[h];
        for (int y = 0; y < h; y++)
        {
            int d = Math.Min(y, h - 1 - y);
            int inset = d == 0 ? 6 : d == 1 ? 3 : d == 2 ? 2 : d == 3 ? 1 : 0;
            rows[y] = new string('.', inset) + new string('#', w - (2 * inset)) + new string('.', inset);
        }

        return rows;
    }

    private static readonly string[] RestartGrid =
    [
        "....####....",
        "..##....##..",
        ".#........#.",
        ".#.........#",
        "...........#",
        "........#..#",
        ".......##..#",
        "......###..#",
        ".#.........#",
        ".#........#.",
        "..##....##..",
        "....####....",
    ];

    private static readonly (char Char, string[] Rows)[] Font =
    [
        ('0', ["#####", "#...#", "#..##", "#.#.#", "##..#", "#...#", "#####"]),
        ('1', ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", "#####"]),
        ('2', ["#####", "....#", "....#", "#####", "#....", "#....", "#####"]),
        ('3', ["#####", "....#", "....#", ".####", "....#", "....#", "#####"]),
        ('4', ["#...#", "#...#", "#...#", "#####", "....#", "....#", "....#"]),
        ('5', ["#####", "#....", "#....", "#####", "....#", "....#", "#####"]),
        ('6', ["#####", "#....", "#....", "#####", "#...#", "#...#", "#####"]),
        ('7', ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."]),
        ('8', ["#####", "#...#", "#...#", "#####", "#...#", "#...#", "#####"]),
        ('9', ["#####", "#...#", "#...#", "#####", "....#", "....#", "#####"]),
        ('A', [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"]),
        ('E', ["#####", "#....", "#....", "####.", "#....", "#....", "#####"]),
        ('G', [".####", "#....", "#....", "#.###", "#...#", "#...#", ".####"]),
        ('H', ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"]),
        ('I', ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"]),
        ('M', ["#...#", "##.##", "#.#.#", "#...#", "#...#", "#...#", "#...#"]),
        ('O', [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."]),
        ('R', ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"]),
        ('V', ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."]),
    ];
}
