using TaskbarDino.Core.Dino;

namespace TaskbarDino.Core.Behavior;

/// <summary>
/// Moves the dino toward a target along the taskbar: it walks when the target is near, runs when it
/// is far, and can hop with a simple gravity arc. Pure math (feet X + a jump height), unit-testable,
/// and all speeds scale with the monitor's UI scale so motion feels the same at any DPI.
/// Re-targeting mid-move is safe (the destination just changes), which is how rapid app switches
/// are absorbed without queueing animations. <see cref="Teleport"/> is reserved for hard cuts.
/// </summary>
public sealed class MoveController
{
    private const float WalkSpeed = 200f;      // px/s at 100%
    private const float RunSpeed = 620f;       // fast, so relocating between far-apart icons feels snappy
    private const float RunDistance = 260f;    // farther than this → run; nearer → walk
    private const float ArriveEpsilon = 1.0f;
    private const float Gravity = 1700f;       // px/s²
    private const float JumpVelocity = 440f;   // initial upward px/s (≈ one body-height hop)

    /// <summary>Trips at least this far (× UI scale) start with a leap; shorter ones just walk or run.</summary>
    public const float HopDistance = 520f;

    private readonly float _scale;

    private float _x;
    private float _targetX;
    private float _jumpHeight;
    private float _jumpVelocity;
    private bool _jumping;
    private bool _run; // gait chosen when the trip starts, so the dino doesn't slow to a walk near the target

    public MoveController(float uiScale) => _scale = MathF.Max(1f, uiScale);

    public float FeetX => _x;

    public float JumpHeight => _jumpHeight;

    public Facing Facing { get; private set; } = Facing.Right;

    public DinoState State { get; private set; } = DinoState.Idle;

    /// <summary>True while the dino still has ground to cover or is airborne.</summary>
    public bool IsBusy => _jumping || MathF.Abs(_targetX - _x) > ArriveEpsilon;

    /// <summary>Instantly relocates (no animation). Use only for hard cuts like a monitor change.</summary>
    public void Teleport(float x)
    {
        _x = x;
        _targetX = x;
        _jumpHeight = 0f;
        _jumpVelocity = 0f;
        _jumping = false;
        State = DinoState.Idle;
    }

    /// <summary>Heads for <paramref name="targetX"/>, optionally kicking off a hop.</summary>
    public void MoveTo(float targetX, bool jump = false)
    {
        _targetX = targetX;
        _run = MathF.Abs(targetX - _x) >= RunDistance * _scale;
        if (jump && !_jumping)
        {
            _jumping = true;
            _jumpVelocity = JumpVelocity * _scale;
        }
    }

    /// <summary>Relocates naturally: walk when near, run when farther, leap off the mark when far.</summary>
    public void Travel(float targetX) => MoveTo(targetX, jump: MathF.Abs(targetX - _x) >= HopDistance * _scale);

    /// <summary>Starts falling from <paramref name="height"/> above the ground (used after a drop).</summary>
    public void FallFrom(float height)
    {
        _jumpHeight = MathF.Max(0f, height);
        _jumpVelocity = 0f;
        _jumping = _jumpHeight > 0f;
    }

    public void Update(float deltaSeconds)
    {
        float remaining = _targetX - _x;
        float distance = MathF.Abs(remaining);

        if (distance > ArriveEpsilon)
        {
            Facing = remaining > 0 ? Facing.Right : Facing.Left;
            float step = (_run ? RunSpeed : WalkSpeed) * _scale * deltaSeconds;
            _x = step >= distance ? _targetX : _x + (MathF.Sign(remaining) * step);
        }
        else if (distance > 0f)
        {
            _x = _targetX; // settle exactly on target within the arrival epsilon
        }

        if (_jumping)
        {
            _jumpHeight += _jumpVelocity * deltaSeconds;
            _jumpVelocity -= Gravity * _scale * deltaSeconds;
            if (_jumpHeight <= 0f)
            {
                _jumpHeight = 0f;
                _jumping = false;
            }
        }

        State = ResolveState();
    }

    private DinoState ResolveState()
    {
        if (_jumping)
        {
            return DinoState.Jumping;
        }

        float distance = MathF.Abs(_targetX - _x);
        if (distance <= ArriveEpsilon)
        {
            return DinoState.Idle;
        }

        return _run ? DinoState.Running : DinoState.Walking;
    }
}
