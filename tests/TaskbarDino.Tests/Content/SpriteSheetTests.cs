using TaskbarDino.Core.Content;
using TaskbarDino.Core.Dino;
using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;
using TaskbarDino.Core.Settings;

namespace TaskbarDino.Tests.Content;

public class SpriteSheetTests
{
    [Theory]
    [InlineData(DinoTheme.Accent)]
    [InlineData(DinoTheme.ChromeGray)]
    [InlineData(DinoTheme.Gold)]
    public void Create_AnyTheme_EveryFrameHasArtInsideItsCell(DinoTheme theme)
    {
        SpriteSheet sheet = DinoSpriteSheet.Create(theme, new Rgb(0, 0x78, 0xD4));
        Assert.Equal(sheet.Width * sheet.Height, sheet.Pixels.Length);

        foreach ((string name, AtlasRegion region) in sheet.Regions)
        {
            PixelRect bounds = sheet.OpaqueBounds(region);
            Assert.False(bounds.IsEmpty, $"frame '{name}' is empty");
            Assert.True(bounds.Right <= region.Width && bounds.Bottom <= region.Height, $"frame '{name}' overflows its cell");
        }
    }

    [Fact]
    public void OpaqueBounds_StandingFrames_ShareTheSameFootprint()
    {
        SpriteSheet sheet = DinoSpriteSheet.Create(DinoTheme.Green);
        PixelRect idle = sheet.OpaqueBounds(sheet[DinoSpriteSheet.Idle]);
        foreach (string name in new[] { DinoSpriteSheet.Blink, DinoSpriteSheet.LookLeft, DinoSpriteSheet.LookRight, DinoSpriteSheet.TailUp, DinoSpriteSheet.Dead, DinoSpriteSheet.Happy })
        {
            PixelRect b = sheet.OpaqueBounds(sheet[name]);
            Assert.Equal(idle.Bottom, b.Bottom);
            Assert.Equal(idle.Right, b.Right);
        }
    }

    [Fact]
    public void OpaqueBounds_DuckFrames_AreLowerThanStanding()
    {
        SpriteSheet sheet = DinoSpriteSheet.Create(DinoTheme.Blue);
        PixelRect idle = sheet.OpaqueBounds(sheet[DinoSpriteSheet.Idle]);
        PixelRect duck = sheet.OpaqueBounds(sheet[DinoSpriteSheet.Duck1]);
        Assert.True(duck.Height < idle.Height);
        Assert.Equal(idle.Bottom, duck.Bottom);
    }

    [Fact]
    public void Create_DifferentThemes_SameMaskDifferentColours()
    {
        SpriteSheet a = DinoSpriteSheet.Create(DinoTheme.Red);
        SpriteSheet b = DinoSpriteSheet.Create(DinoTheme.Purple);
        bool differs = false;
        for (int i = 0; i < a.Pixels.Length; i++)
        {
            Assert.Equal(a.Pixels[i].IsOpaque, b.Pixels[i].IsOpaque);
            differs |= a.Pixels[i] != b.Pixels[i];
        }

        Assert.True(differs);
    }

    [Fact]
    public void Create_AccentTheme_UsesTheAccentColour()
    {
        var accent = new Rgb(0x10, 0x80, 0x40);
        SpriteSheet sheet = DinoSpriteSheet.Create(DinoTheme.Accent, accent);
        Assert.Contains(sheet.Pixels, p => p == accent.ToBgra32());
    }

    [Fact]
    public void CreateIconPixels_Default_IsOpaqueInTheMiddle()
    {
        Bgra32[] icon = DinoSpriteSheet.CreateIconPixels(DinoTheme.ChromeGray, 32);
        Assert.Equal(32 * 32, icon.Length);
        Assert.True(icon[(16 * 32) + 16].IsOpaque);
    }

    [Fact]
    public void BuildClips_Always_CoverEveryStateAndReaction()
    {
        SpriteSheet sheet = DinoSpriteSheet.Create(DinoTheme.Green);
        var states = DinoAnimations.BuildStateClips(sheet);
        var reactions = DinoAnimations.BuildReactionClips(sheet);

        Assert.All(Enum.GetValues<DinoState>(), s => Assert.True(states.ContainsKey(s), $"no clip for state {s}"));
        Assert.All(Enum.GetValues<DinoReaction>(), r => Assert.True(reactions.ContainsKey(r), $"no clip for reaction {r}"));
        Assert.All(reactions.Values, c => Assert.False(c.Loop, $"reaction '{c.Name}' must be a one-shot"));
    }

    [Fact]
    public void Create_GameSheet_HasOutlinedSpritesAndTheWholeFont()
    {
        SpriteSheet sheet = GameSpriteSheet.Create();
        foreach (char c in "0123456789GAMEOVERHI")
        {
            Assert.True(sheet.Regions.ContainsKey(GameSpriteSheet.Glyph(c)), $"missing glyph '{c}'");
        }

        PixelRect cactus = sheet.OpaqueBounds(sheet[GameSpriteSheet.CactusSmall]);
        Assert.False(cactus.IsEmpty);
        Assert.Equal(GameSpriteSheet.BubbleWidth, sheet[GameSpriteSheet.Bubble].Width);
        Assert.Equal(GameSpriteSheet.GroundWidth, sheet[GameSpriteSheet.Ground].Width);
    }
}
