using System.Runtime.InteropServices;

namespace TaskbarDino.Core.Messaging;

/// <summary>
/// A minimal, synchronous, type-keyed publish/subscribe hub used for coarse-grained domain events
/// (foreground changed, taskbar moved, mode changed, …) so layers stay decoupled.
/// <para>
/// Deliberately not used for high-frequency per-frame data — that flows through direct method calls
/// to avoid the per-publish allocation here. Intended for use on the single UI thread.
/// </para>
/// </summary>
public interface IEventBus
{
    /// <summary>Synchronously delivers <paramref name="message"/> to all current subscribers of its type.</summary>
    void Publish<TEvent>(TEvent message)
        where TEvent : notnull;

    /// <summary>Subscribes to events of <typeparamref name="TEvent"/>; dispose the result to unsubscribe.</summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : notnull;
}

/// <inheritdoc cref="IEventBus"/>
public sealed class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];

    public void Publish<TEvent>(TEvent message)
        where TEvent : notnull
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out List<Delegate>? list) || list.Count == 0)
        {
            return;
        }

        // Dispatch over a snapshot so a handler may (un)subscribe during delivery without corrupting iteration.
        foreach (Delegate handler in list.ToArray())
        {
            ((Action<TEvent>)handler)(message);
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);

        ref List<Delegate>? list = ref CollectionsMarshal.GetValueRefOrAddDefault(_handlers, typeof(TEvent), out _);
        list ??= [];
        list.Add(handler);

        return new Subscription(this, typeof(TEvent), handler);
    }

    private void Unsubscribe(Type eventType, Delegate handler)
    {
        if (_handlers.TryGetValue(eventType, out List<Delegate>? list))
        {
            list.Remove(handler);
        }
    }

    private sealed class Subscription(EventBus bus, Type eventType, Delegate handler) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            bus.Unsubscribe(eventType, handler);
        }
    }
}
