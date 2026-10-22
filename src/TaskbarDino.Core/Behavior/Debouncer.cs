namespace TaskbarDino.Core.Behavior;

/// <summary>
/// Collapses a burst of requests (rapid app switching) into one: each <see cref="Request"/> restarts
/// the delay, and the latest value is committed once the delay elapses without another request.
/// </summary>
public sealed class Debouncer<T>(float delaySeconds)
{
    private readonly float _delay = delaySeconds;
    private float _remaining;
    private T? _value;

    public bool Pending { get; private set; }

    /// <summary>Seconds left before the pending value commits (0 when nothing is pending).</summary>
    public float Remaining => Pending ? _remaining : 0f;

    public void Request(T value)
    {
        _value = value;
        _remaining = _delay;
        Pending = true;
    }

    public void Cancel() => Pending = false;

    /// <summary>Advances time; returns <see langword="true"/> (with the latest value) exactly once per burst.</summary>
    public bool Update(float deltaSeconds, out T value)
    {
        if (Pending)
        {
            _remaining -= deltaSeconds;
            if (_remaining <= 0f)
            {
                Pending = false;
                value = _value!;
                return true;
            }
        }

        value = default!;
        return false;
    }
}
