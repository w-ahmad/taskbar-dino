using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Tests.Primitives;

public class PixelRectTests
{
    [Fact]
    public void FromLTRB_ComputesWidthAndHeight()
    {
        PixelRect r = PixelRect.FromLTRB(10, 20, 110, 70);

        Assert.Equal(10, r.X);
        Assert.Equal(20, r.Y);
        Assert.Equal(100, r.Width);
        Assert.Equal(50, r.Height);
        Assert.Equal(110, r.Right);
        Assert.Equal(70, r.Bottom);
    }

    [Fact]
    public void Center_IsMidpoint()
    {
        var r = new PixelRect(0, 0, 100, 40);
        Assert.Equal(new PixelPoint(50, 20), r.Center);
    }

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(0, 0, true)]     // top-left is inclusive
    [InlineData(10, 5, false)]   // right edge is exclusive
    [InlineData(5, 10, false)]   // bottom edge is exclusive
    [InlineData(-1, 5, false)]
    public void Contains_UsesHalfOpenBounds(int x, int y, bool expected)
    {
        var r = new PixelRect(0, 0, 10, 10);
        Assert.Equal(expected, r.Contains(x, y));
    }

    [Fact]
    public void Covers_IsTrueOnlyWhenOtherIsInside()
    {
        var outer = new PixelRect(0, 0, 100, 100);
        Assert.True(new PixelRect(0, 0, 100, 100).Covers(outer));
        Assert.True(new PixelRect(-5, -5, 110, 110).Covers(outer));
        Assert.False(new PixelRect(1, 0, 99, 100).Covers(outer));
    }

    [Fact]
    public void IsEmpty_WhenNonPositiveArea()
    {
        Assert.True(new PixelRect(0, 0, 0, 10).IsEmpty);
        Assert.True(new PixelRect(0, 0, 10, 0).IsEmpty);
        Assert.False(new PixelRect(0, 0, 1, 1).IsEmpty);
    }
}
