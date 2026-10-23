using TaskbarDino.Core.Behavior;

namespace TaskbarDino.Tests.Behavior;

public class DebouncerTests
{
    [Fact]
    public void Update_AfterDelayWithTwoRequests_CommitsLatestValueOnce()
    {
        var d = new Debouncer<string>(0.3f);
        d.Request("a");
        d.Request("b");

        Assert.False(d.Update(0.2f, out _));
        Assert.True(d.Pending);
        Assert.True(d.Update(0.2f, out string v));
        Assert.Equal("b", v);
        Assert.False(d.Pending);
    }

    [Fact]
    public void Request_RepeatedWithinDelay_RestartsTheDelay()
    {
        var d = new Debouncer<int>(0.3f);
        for (int i = 0; i < 5; i++)
        {
            d.Request(i);
            Assert.False(d.Update(0.2f, out _)); // each request lands before the previous delay elapses
        }

        Assert.True(d.Update(0.2f, out int v));
        Assert.Equal(4, v);
    }

    [Fact]
    public void Update_AfterCommit_DoesNotCommitAgain()
    {
        var d = new Debouncer<int>(0.1f);
        d.Request(1);
        Assert.True(d.Update(0.2f, out _));
        Assert.False(d.Update(0.2f, out _));
        Assert.Equal(0f, d.Remaining);
    }

    [Fact]
    public void Cancel_WithPendingValue_DropsIt()
    {
        var d = new Debouncer<int>(0.1f);
        d.Request(1);
        d.Cancel();
        Assert.False(d.Update(1f, out _));
    }
}
