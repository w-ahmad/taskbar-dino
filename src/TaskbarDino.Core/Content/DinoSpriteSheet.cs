using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;
using TaskbarDino.Core.Settings;

namespace TaskbarDino.Core.Content;

/// <summary>
/// The built-in, original pixel-art dino plus its little effect sprites (heart, zzz, "?", "!").
/// One body is authored as a character grid; eyes, legs and overlays are composed programmatically
/// per frame. Colours come from a per-theme three-tone palette, so a theme switch just re-rasterises.
/// Everything is generated at runtime — no third-party art, no image decoding. Frames are 24×24.
/// </summary>
public static class DinoSpriteSheet
{
    public const int FrameWidth = 24;
    public const int FrameHeight = 24;
    public const int AtlasWidth = 24 * 8; // 8 frames per shelf

    // Frame names
    public const string Idle = "idle";
    public const string Blink = "blink";
    public const string LookLeft = "look_left";
    public const string LookRight = "look_right";
    public const string Walk1 = "walk1";
    public const string Walk2 = "walk2";
    public const string Jump = "jump";
    public const string TailUp = "tail_up";
    public const string Yawn = "yawn";
    public const string Sleep = "sleep";
    public const string Scratch1 = "scratch1";
    public const string Scratch2 = "scratch2";
    public const string Stretch = "stretch";
    public const string Duck1 = "duck1";
    public const string Duck2 = "duck2";
    public const string Dead = "dead";
    public const string Happy = "happy";

    // Effect sprites (drawn over the dino by the animation layer)
    public const string FxHeart = "fx_heart";
    public const string FxZzz1 = "fx_zzz1";
    public const string FxZzz2 = "fx_zzz2";
    public const string FxQuestion = "fx_question";
    public const string FxExclaim = "fx_exclaim";

    private static readonly Bgra32 EyeWhite = Bgra32.Rgb(0xF2, 0xF2, 0xF2);
    private static readonly Bgra32 Pupil = Bgra32.Rgb(0x1E, 0x1E, 0x1E);
    private static readonly Bgra32 DarkInk = Bgra32.Rgb(0x2A, 0x2A, 0x2A);
    private static readonly Bgra32 LightInk = Bgra32.Rgb(0xF6, 0xF6, 0xF6);
    private static readonly Bgra32 HeartRed = Bgra32.Rgb(0xE5, 0x3E, 0x5A);
    private static readonly Bgra32 HeartShine = Bgra32.Rgb(0xFF, 0x9A, 0xAC);

    private static readonly Dictionary<DinoTheme, Palette> Palettes = new()
    {
        [DinoTheme.ChromeGray] = new(Bgra32.Rgb(0x35, 0x35, 0x35), Bgra32.Rgb(0x53, 0x53, 0x53), Bgra32.Rgb(0x76, 0x76, 0x76)),
        [DinoTheme.Green] = new(Bgra32.Rgb(0x27, 0x5E, 0x2A), Bgra32.Rgb(0x49, 0x9E, 0x4C), Bgra32.Rgb(0x7B, 0xCF, 0x7E)),
        [DinoTheme.Blue] = new(Bgra32.Rgb(0x27, 0x47, 0x7A), Bgra32.Rgb(0x44, 0x78, 0xC4), Bgra32.Rgb(0x7F, 0xAC, 0xEC)),
        [DinoTheme.Red] = new(Bgra32.Rgb(0x75, 0x27, 0x27), Bgra32.Rgb(0xC2, 0x4A, 0x4A), Bgra32.Rgb(0xE8, 0x80, 0x80)),
        [DinoTheme.Purple] = new(Bgra32.Rgb(0x4C, 0x33, 0x75), Bgra32.Rgb(0x86, 0x59, 0xBE), Bgra32.Rgb(0xB8, 0x97, 0xDE)),
        [DinoTheme.Gold] = new(Bgra32.Rgb(0x75, 0x58, 0x1A), Bgra32.Rgb(0xC2, 0x9A, 0x38), Bgra32.Rgb(0xE8, 0xC8, 0x6E)),
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

    // ---- Art (facing right; 'o' outline, '#' body, '=' belly) ----

    private static readonly string[] Body =
    [
        "........................",
        ".............ooooooo....",
        "............o#######o...",
        "............o#######o...",
        "............o#######o...",
        "............o#######o...",
        "............o#######o...",
        "............o###oooo....",
        "...........o#######o....",
        "......oo...o######o.....",
        ".....o##o..o#######o....",
        "....o##ooooo########o...",
        "...o############ooo#o...",
        "..o##############o##o...",
        "..o#################o...",
        "..o################o....",
        "..o###==========##o.....",
        "..o##============##o....",
        "..o##############o......",
    ];

    private static readonly string[] BodyTailUp =
    [
        "........................",
        ".............ooooooo....",
        "............o#######o...",
        "............o#######o...",
        "............o#######o...",
        "............o#######o...",
        "............o#######o...",
        "............o###oooo....",
        "....oo.....o#######o....",
        "...o##o....o######o.....",
        "...o##oo...o#######o....",
        "....o##ooooo########o...",
        "...o############ooo#o...",
        "..o##############o##o...",
        "..o#################o...",
        "..o################o....",
        "..o###==========##o.....",
        "..o##============##o....",
        "..o##############o......",
    ];

    private static readonly string[] BodyYawn =
    [
        "........................",
        ".............ooooooo....",
        "............o#######o...",
        "............o#######o...",
        "............o#######o...",
        "............o#######o...",
        "............o###oooo....",
        "............o###o...o...",
        "...........o####oooo....",
        "......oo...o######o.....",
        ".....o##o..o#######o....",
        "....o##ooooo########o...",
        "...o############ooo#o...",
        "..o##############o##o...",
        "..o#################o...",
        "..o################o....",
        "..o###==========##o.....",
        "..o##============##o....",
        "..o##############o......",
    ];

    // Low, stretched-out posture for ducking under a pterodactyl (bottom-aligned, 14 rows tall).
    private static readonly string[] BodyDuck =
    [
        "..............ooooooo...",
        ".............o#######o..",
        "....oo......o#########o.",
        "...o##oooooo#####oooo#o.",
        "..o###################o.",
        "..o##################o..",
        "..o#####==========##o...",
        "...o####==========#o....",
        "....oo############o.....",
    ];

    private static readonly string[] LegsStand =
    [
        "....##.....##...........",
        "....##.....##...........",
        "....##.....##...........",
        "...o##o...o##o..........",
        "...####...####..........",
    ];

    private static readonly string[] LegsWalkA =
    [
        "...##......##...........",
        "..##.......##...........",
        "..##........##..........",
        ".o##o......o##o.........",
        ".###........###.........",
    ];

    private static readonly string[] LegsWalkB =
    [
        ".....##..##.............",
        ".....##..##.............",
        ".....##..##.............",
        "....o##..##o............",
        "....###..###............",
    ];

    private static readonly string[] LegsJump =
    [
        "....###..###............",
        ".....#....#.............",
        "........................",
        "........................",
        "........................",
    ];

    private static readonly string[] LegsScratchA =
    [
        "...##o.....##...........",
        "..o###.....##...........",
        "..####.....##...........",
        "..........o##o..........",
        "..........####..........",
    ];

    private static readonly string[] LegsScratchB =
    [
        "....##.....##...........",
        "...o##o....##...........",
        "...###.....##...........",
        "..........o##o..........",
        "..........####..........",
    ];

    private static readonly string[] LegsTiptoe =
    [
        "....##.....##...........",
        "....##.....##...........",
        "....##.....##...........",
        "....##.....##...........",
        "....o#....o#............",
    ];

    private static readonly string[] LegsSit =
    [
        "...o##o...o##o..........",
        "...####...####..........",
    ];

    private static readonly string[] LegsDuckA =
    [
        "......##.....##.........",
        ".....o##o...o##o........",
        ".....####...####........",
    ];

    private static readonly string[] LegsDuckB =
    [
        "......##.....##.........",
        "......##....o##o........",
        ".....o##o...####........",
    ];

    private static readonly string[] HeartGrid =
    [
        ".rr.rr.",
        "rsrrrrr",
        "rrrrrrr",
        ".rrrrr.",
        "..rrr..",
        "...r...",
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

    /// <summary>A 24×24 frame: body grid at a vertical offset, legs grid, and an eye variant at a position.</summary>
    private readonly record struct Frame(string Name, string[] Body, int BodyY, string[] Legs, int LegsY, Eye Eye, int EyeX, int EyeY);

    private const int StdEyeX = 14;
    private const int StdEyeY = 4;

    private static readonly Frame[] Frames =
    [
        new(Idle, Body, 0, LegsStand, 19, Eye.Open, StdEyeX, StdEyeY),
        new(Blink, Body, 0, LegsStand, 19, Eye.Closed, StdEyeX, StdEyeY),
        new(LookLeft, Body, 0, LegsStand, 19, Eye.Left, StdEyeX, StdEyeY),
        new(LookRight, Body, 0, LegsStand, 19, Eye.Right, StdEyeX, StdEyeY),
        new(Walk1, Body, 0, LegsWalkA, 19, Eye.Open, StdEyeX, StdEyeY),
        new(Walk2, Body, 0, LegsWalkB, 19, Eye.Open, StdEyeX, StdEyeY),
        new(Jump, Body, 0, LegsJump, 19, Eye.Open, StdEyeX, StdEyeY),
        new(TailUp, BodyTailUp, 0, LegsStand, 19, Eye.Open, StdEyeX, StdEyeY),
        new(Yawn, BodyYawn, 0, LegsStand, 19, Eye.Closed, StdEyeX, StdEyeY),
        new(Sleep, Body, 3, LegsSit, 22, Eye.Closed, StdEyeX, StdEyeY + 3),
        new(Scratch1, Body, 0, LegsScratchA, 19, Eye.Open, StdEyeX, StdEyeY),
        new(Scratch2, Body, 0, LegsScratchB, 19, Eye.Open, StdEyeX, StdEyeY),
        new(Stretch, Body, -1, LegsTiptoe, 18, Eye.Closed, StdEyeX, StdEyeY - 1),
        new(Duck1, BodyDuck, 12, LegsDuckA, 21, Eye.Open, 16, 14),
        new(Duck2, BodyDuck, 12, LegsDuckB, 21, Eye.Open, 16, 14),
        new(Dead, Body, 0, LegsStand, 19, Eye.Dead, StdEyeX, StdEyeY),
        new(Happy, BodyTailUp, 0, LegsStand, 19, Eye.Happy, StdEyeX, StdEyeY),
    ];

    private static readonly (string Name, string[] Rows, bool Light)[] Effects =
    [
        (FxHeart, HeartGrid, false),
        (FxZzz1, Zzz1Grid, false),
        (FxZzz2, Zzz2Grid, false),
        (FxQuestion, QuestionGrid, false),
        (FxExclaim, ExclaimGrid, false),
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

        foreach ((string name, string[] rows, _) in Effects)
        {
            cells[i++] = (name, rows[0].Length + 2, rows.Length + 2); // room for the outline
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
            PixelArt.Paint(pixels, AtlasWidth, cell, 0, f.BodyY, f.Body, ink);
            PixelArt.Paint(pixels, AtlasWidth, cell, 0, f.LegsY, f.Legs, ink);
            PaintEye(pixels, AtlasWidth, cell, f.Eye, f.EyeX, f.EyeY);
        }

        var heartInk = new PixelArt.Ink(new[] { ('r', HeartRed), ('s', HeartShine) });
        var darkInk = new PixelArt.Ink(new[] { ('k', DarkInk) });
        foreach ((string name, string[] rows, bool light) in Effects)
        {
            PixelRect cell = Regions[name].Source;
            PixelArt.Paint(pixels, AtlasWidth, cell, 1, 1, rows, name == FxHeart ? heartInk : darkInk);
            PixelArt.Outline(pixels, AtlasWidth, cell, name == FxHeart ? DarkInk : LightInk);
        }

        return new SpriteSheet(AtlasWidth, AtlasHeight, pixels, Regions);
    }

    /// <summary>Renders just the idle pose, scaled to <paramref name="size"/>², for a tray/app icon.</summary>
    public static Bgra32[] CreateIconPixels(DinoTheme theme, int size, Rgb accent = default)
    {
        Palette palette = PaletteFor(theme, accent);
        var frame = new Bgra32[FrameWidth * FrameHeight];
        var cell = new PixelRect(0, 0, FrameWidth, FrameHeight);
        var ink = new PixelArt.Ink(new[] { ('o', palette.Outline), ('#', palette.Body), ('=', palette.Belly) });
        PixelArt.Paint(frame, FrameWidth, cell, 0, 0, Body, ink);
        PixelArt.Paint(frame, FrameWidth, cell, 0, 19, LegsStand, ink);
        PaintEye(frame, FrameWidth, cell, Eye.Open, StdEyeX, StdEyeY);

        // The art occupies columns 1..22 and rows 1..23; crop to that 22×23 box so the icon fills its cell.
        const int cropX = 1, cropY = 1, cropW = 22, cropH = 23;
        var icon = new Bgra32[size * size];
        for (int y = 0; y < size; y++)
        {
            int sy = cropY + (y * cropH / size);
            for (int x = 0; x < size; x++)
            {
                int sx = cropX + (x * cropW / size);
                icon[(y * size) + x] = frame[(sy * FrameWidth) + sx];
            }
        }

        return icon;
    }

    private static Palette PaletteFor(DinoTheme theme, Rgb accent)
    {
        if (theme != DinoTheme.Accent)
        {
            return Palettes.TryGetValue(theme, out Palette p) ? p : Palettes[DinoTheme.ChromeGray];
        }

        Rgb a = accent == default ? AccentFallback : accent;
        return new Palette(a.Scale(0.5f).ToBgra32(), a.ToBgra32(), a.Lighten(0.35f).ToBgra32());
    }

    private static void PaintEye(Bgra32[] buffer, int stride, PixelRect cell, Eye eye, int ex, int ey)
    {
        const int w = 4, h = 3;
        void Set(int x, int y, Bgra32 c) => buffer[((cell.Y + ey + y) * stride) + cell.X + ex + x] = c;

        switch (eye)
        {
            case Eye.Closed:
                for (int x = 0; x < w; x++) Set(x, 1, Pupil);
                return;
            case Eye.Happy:
                Set(1, 0, Pupil); Set(2, 0, Pupil); Set(0, 1, Pupil); Set(3, 1, Pupil);
                return;
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Set(x, y, EyeWhite);
            }
        }

        if (eye == Eye.Dead)
        {
            Set(0, 0, Pupil); Set(3, 0, Pupil); Set(1, 1, Pupil); Set(2, 1, Pupil); Set(0, 2, Pupil); Set(3, 2, Pupil);
            return;
        }

        int pupilX = eye switch { Eye.Left => 0, Eye.Right => 2, _ => 1 };
        Set(pupilX, 1, Pupil);
        Set(pupilX + 1, 1, Pupil);
    }
}
