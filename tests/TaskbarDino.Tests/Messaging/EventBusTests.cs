using TaskbarDino.Core.Messaging;

namespace TaskbarDino.Tests.Messaging;

public class EventBusTests
{
    private sealed record Ping(int Value);

    private sealed record Pong(string Text);

    [Fact]
    public void Publish_DeliversToSubscriber()
    {
        var bus = new EventBus();
        int received = 0;
        bus.Subscribe<Ping>(p => received = p.Value);

        bus.Publish(new Ping(42));

        Assert.Equal(42, received);
    }

    [Fact]
    public void Publish_DeliversToAllSubscribers()
    {
        var bus = new EventBus();
        int count = 0;
        bus.Subscribe<Ping>(_ => count++);
        bus.Subscribe<Ping>(_ => count++);

        bus.Publish(new Ping(1));

        Assert.Equal(2, count);
    }

    [Fact]
    public void Publish_IsTypeSegregated()
    {
        var bus = new EventBus();
        bool pongCalled = false;
        bus.Subscribe<Pong>(_ => pongCalled = true);

        bus.Publish(new Ping(1));

        Assert.False(pongCalled);
    }

    [Fact]
    public void Dispose_Unsubscribes()
    {
        var bus = new EventBus();
        int count = 0;
        IDisposable subscription = bus.Subscribe<Ping>(_ => count++);

        bus.Publish(new Ping(1));
        subscription.Dispose();
        bus.Publish(new Ping(2));

        Assert.Equal(1, count);
    }

    [Fact]
    public void Handler_MayUnsubscribeDuringDispatch_WithoutError()
    {
        var bus = new EventBus();
        int count = 0;
        IDisposable? sub = null;
        sub = bus.Subscribe<Ping>(_ =>
        {
            count++;
            sub!.Dispose();
        });

        bus.Publish(new Ping(1));
        bus.Publish(new Ping(2));

        Assert.Equal(1, count);
    }

    [Fact]
    public void Publish_WithNoSubscribers_IsNoOp()
    {
        var bus = new EventBus();
        bus.Publish(new Ping(1)); // must not throw
    }
}
