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
    private const float WalkSpeed = 230f;      // px/s at 100%
    private const float RunSpeed = 640f;       // fast, so relocating between app icons feels snappy
    private const float RunDistance = 90f;     // farther than this → run
    private const float ArriveEpsilon = 1.0f;
    private const float Gravity = 1600f;       // px/s²
    private const float JumpVelocity = 380f;   // initial upward px/s (≈ one body-height hop)

    private readonly float _scale;

    private float _x;
    private float _targetX;
    private float _jumpHeight;
    private float _jumpVelocity;
    private bool _jumping;

    public MoveController(float uiScale) => _scale = MathF.Max(1f, uiScale);

    public float FeetX => _x;

    public float TargetX => _targetX;

    public float JumpHeight => _jumpHeight;

    public Facing Facing { get; private set; } = Facing.Right;

    public DinoState State { get; private set; } = DinoState.Idle;

    /// <summary>True while the dino still has ground to cover or is airborne.</summary>
    public bool IsBusy => _jumping || MathF.Abs(_targetX - _x) > ArriveEpsilon;

    /// <summary>The maximum height a hop reaches.</summary>
    public float MaxJumpHeight => (JumpVelocity * JumpVelocity) / (2f * Gravity) * _scale;

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
        if (jump && !_jumping)
        {
            _jumping = true;
            _jumpVelocity = JumpVelocity * _scale;
        }
    }

    /// <summary>Starts falling from <paramref name="height"/> above the ground (used after a drop).</summary>
    public void FallFrom(float height)
    {
        _jumpHeight = MathF.Max(0f, height);
        _jumpVelocity = 0f;
        _jumping = _jumpHeight > 0f;
    }

    public void Face(Facing facing) => Facing = facing;

    public void Update(float deltaSeconds)
    {
        float remaining = _targetX - _x;
        float distance = MathF.Abs(remaining);

        if (distance > ArriveEpsilon)
        {
            Facing = remaining > 0 ? Facing.Right : Facing.Left;
            bool run = distance >= RunDistance * _scale;
            float step = (run ? RunSpeed : WalkSpeed) * _scale * deltaSeconds;
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

        return distance >= RunDistance * _scale ? DinoState.Running : DinoState.Walking;
    }
}
