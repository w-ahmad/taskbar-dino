using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;
using TaskbarDino.Core.Settings;

namespace TaskbarDino.Core.Content;

/// <summary>
/// The built-in pixel-art dino (48×48 frames) plus its effect sprites (heart, zzz, "?", "!", ball,
/// sprout, confetti). The poses come from <see cref="DinoArt"/> (generated from shapes so every
/// pose shares the same body); eyes and effects are composed here. Colours come from a per-theme
/// three-tone palette, so a theme switch just re-rasterises. No image decoding, no third-party art.
/// </summary>
public static class DinoSpriteSheet
{
    public const int FrameWidth = 48;
    public const int FrameHeight = 48;
    public const int AtlasWidth = 48 * 8; // 8 frames per shelf

    // Frame names
    public const string Idle = "idle";
    public const string Blink = "blink";
    public const string LookLeft = "look_left";
    public const string LookRight = "look_right";
    public const string Happy = "happy";
    public const string Dead = "dead";
    public const string Walk1 = "walk1";
    public const string Walk2 = "walk2";
    public const string Walk3 = "walk3";
    public const string Walk4 = "walk4";
    public const string Jump = "jump";
    public const string TailUp = "tail_up";
    public const string Yawn = "yawn";
    public const string Sleep = "sleep";
    public const string Scratch1 = "scratch1";
    public const string Scratch2 = "scratch2";
    public const string Stretch = "stretch";
    public const string Duck1 = "duck1";
    public const string Duck2 = "duck2";
    public const string Eat = "eat";

    // Effect sprites (drawn over the dino by the animation layer)
    public const string FxHeart = "fx_heart";
    public const string FxZzz1 = "fx_zzz1";
    public const string FxZzz2 = "fx_zzz2";
    public const string FxQuestion = "fx_question";
    public const string FxExclaim = "fx_exclaim";
    public const string FxBall1 = "fx_ball1";
    public const string FxBall2 = "fx_ball2";
    public const string FxSprout1 = "fx_sprout1";
    public const string FxSprout2 = "fx_sprout2";
    public const string FxSprout3 = "fx_sprout3";
    public const string FxConfetti1 = "fx_confetti1";
    public const string FxConfetti2 = "fx_confetti2";

    private static readonly Bgra32 EyeWhite = Bgra32.Rgb(0xF4, 0xF4, 0xF4);
    private static readonly Bgra32 Pupil = Bgra32.Rgb(0x1C, 0x1C, 0x1C);
    private static readonly Bgra32 DarkInk = Bgra32.Rgb(0x2A, 0x2A, 0x2A);
    private static readonly Bgra32 LightInk = Bgra32.Rgb(0xF6, 0xF6, 0xF6);

    private static readonly PixelArt.Ink EffectInk = new(new[]
    {
        ('k', DarkInk),
        ('r', Bgra32.Rgb(0xE5, 0x3E, 0x5A)),   // heart
        ('s', Bgra32.Rgb(0xFF, 0x9A, 0xAC)),   // heart shine
        ('b', Bgra32.Rgb(0xF2, 0x7A, 0xA8)),   // ball
        ('w', Bgra32.Rgb(0xFF, 0xF0, 0xF5)),   // ball shine
        ('g', Bgra32.Rgb(0x4C, 0xAF, 0x50)),   // sprout leaves
        ('d', Bgra32.Rgb(0x2E, 0x7D, 0x32)),   // sprout stem
        ('y', Bgra32.Rgb(0xFF, 0xC1, 0x07)),   // confetti
        ('c', Bgra32.Rgb(0x29, 0xB6, 0xF6)),
        ('m', Bgra32.Rgb(0xAB, 0x47, 0xBC)),
        ('o', Bgra32.Rgb(0xFF, 0x70, 0x43)),
    });

    private static readonly Dictionary<DinoTheme, Palette> Palettes = new()
    {
        [DinoTheme.ChromeGray] = new(Bgra32.Rgb(0x2F, 0x2F, 0x2F), Bgra32.Rgb(0x53, 0x53, 0x53), Bgra32.Rgb(0x7A, 0x7A, 0x7A)),
        [DinoTheme.Green] = new(Bgra32.Rgb(0x24, 0x5B, 0x27), Bgra32.Rgb(0x49, 0x9E, 0x4C), Bgra32.Rgb(0x82, 0xD3, 0x85)),
        [DinoTheme.Blue] = new(Bgra32.Rgb(0x23, 0x43, 0x76), Bgra32.Rgb(0x44, 0x78, 0xC4), Bgra32.Rgb(0x86, 0xB0, 0xEE)),
        [DinoTheme.Red] = new(Bgra32.Rgb(0x70, 0x24, 0x24), Bgra32.Rgb(0xC2, 0x4A, 0x4A), Bgra32.Rgb(0xEC, 0x88, 0x88)),
        [DinoTheme.Purple] = new(Bgra32.Rgb(0x47, 0x2F, 0x70), Bgra32.Rgb(0x86, 0x59, 0xBE), Bgra32.Rgb(0xBD, 0x9E, 0xE2)),
        [DinoTheme.Gold] = new(Bgra32.Rgb(0x70, 0x54, 0x18), Bgra32.Rgb(0xC2, 0x9A, 0x38), Bgra32.Rgb(0xEC, 0xCD, 0x78)),
    };

    private static readonly Rgb AccentFallback = new(0x2E, 0x78, 0xD4);

    private readonly record struct Palette(Bgra32 Outline, Bgra32 Body, Bgra32 Belly);

    private enum Eye
    {
        Open,
        Closed,
        Left,
        Right,
        Dead,
        Happy,
    }

    /// <summary>A frame: a pose grid, which eye to paint, and the pose name used to place the eye.</summary>
    private readonly record struct Frame(string Name, string Pose, string[] Grid, Eye Eye);

    private static readonly Frame[] Frames =
    [
        new(Idle, "idle", DinoArt.Idle, Eye.Open),
        new(Blink, "idle", DinoArt.Idle, Eye.Closed),
        new(LookLeft, "idle", DinoArt.Idle, Eye.Left),
        new(LookRight, "idle", DinoArt.Idle, Eye.Right),
        new(Happy, "tail_up", DinoArt.TailUp, Eye.Happy),
        new(Dead, "idle", DinoArt.Idle, Eye.Dead),
        new(Walk1, "walk1", DinoArt.Walk1, Eye.Open),
        new(Walk2, "walk2", DinoArt.Walk2, Eye.Open),
        new(Walk3, "walk3", DinoArt.Walk3, Eye.Open),
        new(Walk4, "walk4", DinoArt.Walk4, Eye.Open),
        new(Jump, "jump", DinoArt.Jump, Eye.Open),
        new(TailUp, "tail_up", DinoArt.TailUp, Eye.Open),
        new(Yawn, "yawn", DinoArt.Yawn, Eye.Closed),
        new(Sleep, "lie", DinoArt.Lie, Eye.Closed),
        new(Scratch1, "scratch1", DinoArt.Scratch1, Eye.Open),
        new(Scratch2, "scratch2", DinoArt.Scratch2, Eye.Open),
        new(Stretch, "stretch", DinoArt.Stretch, Eye.Closed),
        new(Duck1, "duck1", DinoArt.Duck1, Eye.Open),
        new(Duck2, "duck2", DinoArt.Duck2, Eye.Open),
        new(Eat, "eat", DinoArt.Eat, Eye.Open),
    ];

    // ---- Effect art ----

    private static readonly string[] HeartGrid =
    [
        "...rrr....rrr..",
        "..rsrrr..rrrrr.",
        ".rrsrrrr.rrrrrr",
        ".rrrrrrrrrrrrrr",
        ".rrrrrrrrrrrrrr",
        "..rrrrrrrrrrrr.",
        "...rrrrrrrrrr..",
        "....rrrrrrrr...",
        ".....rrrrrr....",
        "......rrrr.....",
        ".......rr......",
    ];

    private static readonly string[] Zzz1Grid =
    [
        "......kkk",
        "........k",
        ".......k.",
        "......kkk",
        "..kkk....",
        "....k....",
        "...k.....",
        "..kkk....",
    ];

    private static readonly string[] Zzz2Grid =
    [
        "....kkkk.",
        ".......k.",
        "......k..",
        ".....k...",
        "....kkkk.",
        "kkk......",
        "..k......",
        ".k.......",
        "kkk......",
    ];

    private static readonly string[] QuestionGrid =
    [
        ".kkk.",
        "k...k",
        "....k",
        "...k.",
        "..k..",
        ".....",
        "..k..",
    ];

    private static readonly string[] ExclaimGrid =
    [
        ".k.",
        ".k.",
        ".k.",
        ".k.",
        ".k.",
        "...",
        ".k.",
    ];

    private static readonly string[] Ball1Grid =
    [
        "....bbbb....",
        "..bbbbbbbb..",
        ".bbwwbbbbbb.",
        ".bwwbbbbbbb.",
        "bbwbbbbbbbbb",
        "bbbbbbbbbbbb",
        "bbbbbbbbbbbb",
        "bbbbbbbbbbbb",
        ".bbbbbbbbbb.",
        ".bbbbbbbbbb.",
        "..bbbbbbbb..",
        "....bbbb....",
    ];

    private static readonly string[] Ball2Grid =
    [
        "....bbbb....",
        "..bbbbbbbb..",
        ".bbbbbbwwbb.",
        ".bbbbbbbwwb.",
        "bbbbbbbbbwbb",
        "bbbbbbbbbbbb",
        "bbbbbbbbbbbb",
        "bbbbbbbbbbbb",
        ".bbbbbbbbbb.",
        ".bbbbbbbbbb.",
        "..bbbbbbbb..",
        "....bbbb....",
    ];

    private static readonly string[] Sprout1Grid =
    [
        "....gg......",
        "...gggg.....",
        "...gggg..gg.",
        "....dd..gggg",
        "....dd.gggg.",
        ".gg.dddd....",
        "gggg.dd.....",
        ".gggddd.....",
        "....dd......",
        "....dd......",
        "...kkkk.....",
        "..kkkkkk....",
    ];

    private static readonly string[] Sprout2Grid =
    [
        "............",
        "............",
        "............",
        "............",
        "....dd......",
        ".gg.dddd....",
        "gggg.dd.....",
        ".gggddd.....",
        "....dd......",
        "....dd......",
        "...kkkk.....",
        "..kkkkkk....",
    ];

    private static readonly string[] Sprout3Grid =
    [
        "............",
        "............",
        "............",
        "............",
        "............",
        "............",
        "............",
        "............",
        "....dd......",
        "....dd......",
        "...kkkk.....",
        "..kkkkkk....",
    ];

    private static readonly string[] Confetti1Grid =
    [
        ".y....c.....m...o.....y.",
        "...m.....y......c.......",
        "c....o.......m....y...m.",
        "........c..y....o.......",
        ".o...y.......c......c...",
        "....m.....o.......y.....",
        "..c.....y....m..o....m..",
        ".....o.....c.......y....",
        "y.......m.....o...c.....",
        "...c.........y.....m..o.",
    ];

    private static readonly string[] Confetti2Grid =
    [
        "...c....y....o.....m....",
        ".m....o.....c....y......",
        "......y..m.....c.....o..",
        "..o.........y....m......",
        "y....c....o.......c...y.",
        "...m....y....m..........",
        ".c........o....y...c....",
        "....y..c.....m.....o....",
        "..o.....m......c......y.",
        "m....y.....o.....y...c..",
    ];

    private static readonly (string Name, string[] Rows, int Scale, bool Halo)[] Effects =
    [
        (FxHeart, HeartGrid, 1, false),
        (FxZzz1, Zzz1Grid, 2, true),
        (FxZzz2, Zzz2Grid, 2, true),
        (FxQuestion, QuestionGrid, 2, true),
        (FxExclaim, ExclaimGrid, 2, true),
        (FxBall1, Ball1Grid, 1, false),
        (FxBall2, Ball2Grid, 1, false),
        (FxSprout1, Sprout1Grid, 1, false),
        (FxSprout2, Sprout2Grid, 1, false),
        (FxSprout3, Sprout3Grid, 1, false),
        (FxConfetti1, Confetti1Grid, 1, false),
        (FxConfetti2, Confetti2Grid, 1, false),
    ];

    public static IReadOnlyDictionary<string, AtlasRegion> Regions { get; }

    public static int AtlasHeight { get; }

    static DinoSpriteSheet()
    {
        var cells = new (string, int, int)[Frames.Length + Effects.Length];
        int i = 0;
        foreach (Frame f in Frames)
        {
            cells[i++] = (f.Name, FrameWidth, FrameHeight);
        }

        foreach ((string name, string[] rows, int scale, _) in Effects)
        {
            cells[i++] = (name, (rows[0].Length * scale) + 2, (rows.Length * scale) + 2); // room for the outline
        }

        Regions = PixelArt.Pack(cells, AtlasWidth, 0, out int height);
        AtlasHeight = height;
    }

    /// <summary>Rasterises the whole atlas for a theme into a premultiplied BGRA sheet for GPU upload.</summary>
    public static SpriteSheet Create(DinoTheme theme, Rgb accent = default)
    {
        Palette palette = PaletteFor(theme, accent);
        var pixels = new Bgra32[AtlasWidth * AtlasHeight];
        var ink = new PixelArt.Ink(new[] { ('o', palette.Outline), ('#', palette.Body), ('=', palette.Belly) });

        foreach (Frame f in Frames)
        {
            PixelRect cell = Regions[f.Name].Source;
            PixelArt.Paint(pixels, AtlasWidth, cell, 0, 0, f.Grid, ink);
            (int ex, int ey) = DinoArt.Eye(f.Pose);
            PaintEye(pixels, AtlasWidth, cell, f.Eye, ex, ey);
        }

        foreach ((string name, string[] rows, int scale, bool halo) in Effects)
        {
            PixelRect cell = Regions[name].Source;
            PixelArt.Paint(pixels, AtlasWidth, cell, 1, 1, scale == 1 ? rows : PixelArt.Upscale(rows, scale), EffectInk);
            if (name is not (FxConfetti1 or FxConfetti2))
            {
                PixelArt.Outline(pixels, AtlasWidth, cell, halo ? LightInk : DarkInk);
            }
        }

        return new SpriteSheet(AtlasWidth, AtlasHeight, pixels, Regions);
    }

    /// <summary>Renders the idle pose, scaled to <paramref name="size"/>², for a tray/app icon.</summary>
    public static Bgra32[] CreateIconPixels(DinoTheme theme, int size, Rgb accent = default)
    {
        Palette palette = PaletteFor(theme, accent);
        var frame = new Bgra32[FrameWidth * FrameHeight];
        var cell = new PixelRect(0, 0, FrameWidth, FrameHeight);
        var ink = new PixelArt.Ink(new[] { ('o', palette.Outline), ('#', palette.Body), ('=', palette.Belly) });
        PixelArt.Paint(frame, FrameWidth, cell, 0, 0, DinoArt.Idle, ink);
        (int ex, int ey) = DinoArt.Eye("idle");
        PaintEye(frame, FrameWidth, cell, Eye.Open, ex, ey);

        // Crop to the art's bounding box so the icon fills its cell, then resample.
        var sheet = new SpriteSheet(FrameWidth, FrameHeight, frame, Regions);
        PixelRect crop = sheet.OpaqueBounds(new AtlasRegion(cell));
        int side = Math.Max(crop.Width, crop.Height);
        int originX = crop.X - ((side - crop.Width) / 2);
        int originY = crop.Y - ((side - crop.Height) / 2);

        var icon = new Bgra32[size * size];
        for (int y = 0; y < size; y++)
        {
            int sy = originY + (y * side / size);
            for (int x = 0; x < size; x++)
            {
                int sx = originX + (x * side / size);
                icon[(y * size) + x] = sx >= 0 && sy >= 0 && sx < FrameWidth && sy < FrameHeight ? frame[(sy * FrameWidth) + sx] : default;
            }
        }

        return icon;
    }

    /// <summary>The body colour of a theme (for menu swatches).</summary>
    public static Rgb ThemeColor(DinoTheme theme, Rgb accent)
    {
        Bgra32 body = PaletteFor(theme, accent).Body;
        return new Rgb(body.R, body.G, body.B);
    }

    private static Palette PaletteFor(DinoTheme theme, Rgb accent)
    {
        if (theme != DinoTheme.Accent)
        {
            return Palettes.TryGetValue(theme, out Palette p) ? p : Palettes[DinoTheme.ChromeGray];
        }

        Rgb a = accent == default ? AccentFallback : accent;
        return new Palette(a.Scale(0.45f).ToBgra32(), a.ToBgra32(), a.Lighten(0.4f).ToBgra32());
    }

    private static void PaintEye(Bgra32[] buffer, int stride, PixelRect cell, Eye eye, int ex, int ey)
    {
        void Set(int x, int y, Bgra32 c) => buffer[((cell.Y + ey + y) * stride) + cell.X + ex + x] = c;

        switch (eye)
        {
            case Eye.Closed:
                for (int x = 0; x < 5; x++) { Set(x, 2, Pupil); Set(x, 3, Pupil); }
                return;
            case Eye.Happy:
                Set(2, 1, Pupil); Set(1, 2, Pupil); Set(3, 2, Pupil); Set(0, 3, Pupil); Set(4, 3, Pupil);
                return;
        }

        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                Set(x, y, EyeWhite);
            }
        }

        if (eye == Eye.Dead)
        {
            Set(0, 0, Pupil); Set(4, 0, Pupil); Set(1, 1, Pupil); Set(3, 1, Pupil); Set(2, 2, Pupil);
            Set(1, 3, Pupil); Set(3, 3, Pupil); Set(0, 4, Pupil); Set(4, 4, Pupil);
            return;
        }

        int px = eye switch { Eye.Left => 0, Eye.Right => 3, _ => 2 };
        Set(px, 1, Pupil); Set(px + 1, 1, Pupil);
        Set(px, 2, Pupil); Set(px + 1, 2, Pupil);
    }
}
