using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Game;

/// <summary>
/// The player dino. It holds a fixed horizontal spot (its taskbar icon) and only moves vertically:
/// a variable-height hop (holding the key floats it higher), a fast drop when Down is pressed in
/// the air, and a duck that halves its height on the ground. Pure logic on a fixed 60 Hz step.
/// </summary>
public sealed class Trex
{
    private bool _jumping;
    private bool _jumpHeld;
    private bool _duckHeld;
    private float _velocity;   // >0 rising, <0 falling (units/frame)
    private float _jumpOffset; // units above the ground line (0 = grounded)
    private bool _alive = true;

    /// <summary>Left edge of the dino in logical units (set to the taskbar-icon position).</summary>
    public float X { get; set; }

    /// <summary>How far the feet are above the ground line (0 grounded), for the renderer.</summary>
    public float JumpOffset => _jumpOffset;

    public bool IsJumping => _jumping;

    public bool IsDucking => _duckHeld && !_jumping && _alive;

    public bool IsAlive => _alive;

    public TrexPose Pose => !_alive ? TrexPose.Dead : _jumping ? TrexPose.Jumping : IsDucking ? TrexPose.Ducking : TrexPose.Running;

    /// <summary>Collision bounds in logical units (shorter while ducking).</summary>
    public RectF Bounds
    {
        get
        {
            float feet = GameConfig.GroundY - _jumpOffset;
            float height = IsDucking ? GameConfig.DuckHeight : GameConfig.DinoHeight;
            return new RectF(X, feet - height, GameConfig.DinoWidth, height);
        }
    }

    public void Reset()
    {
        _jumping = false;
        _jumpHeld = false;
        _duckHeld = false;
        _velocity = 0f;
        _jumpOffset = 0f;
        _alive = true;
    }

    /// <summary>Begins a hop if grounded (no double-jump). The hop's height grows while the key is held.</summary>
    public void StartJump()
    {
        if (_jumping || !_alive)
        {
            return;
        }

        _jumping = true;
        _jumpHeld = true;
        _velocity = GameConfig.InitialJumpVelocity;
    }

    /// <summary>Sets whether the jump key is currently held (releasing early makes a shorter hop).</summary>
    public void SetJumpHeld(bool held) => _jumpHeld = held;

    /// <summary>Sets whether Down is held: ducks on the ground, slams down in the air.</summary>
    public void SetDuck(bool held)
    {
        _duckHeld = held;
        if (held && _jumping && _velocity > GameConfig.DropVelocity)
        {
            _velocity = GameConfig.DropVelocity;
        }
    }

    public void Kill() => _alive = false;

    public void Step()
    {
        if (!_jumping)
        {
            return;
        }

        _jumpOffset += _velocity;

        // Hard ceiling: a held jump floats up to MaxHoldHeight then falls.
        if (_jumpOffset >= GameConfig.MaxHoldHeight && _velocity > 0f)
        {
            _jumpOffset = GameConfig.MaxHoldHeight;
            _velocity = 0f;
        }

        // Holding the key while still rising applies weaker gravity, so the dino floats higher and
        // stays airborne longer — enough to clear clustered obstacles.
        bool floating = _jumpHeld && _velocity > 0f && _jumpOffset < GameConfig.MaxHoldHeight;
        float gravity = floating ? GameConfig.Gravity * GameConfig.HoldGravityFactor : GameConfig.Gravity;
        _velocity -= gravity;

        if (_jumpOffset <= 0f)
        {
            _jumpOffset = 0f;
            _velocity = 0f;
            _jumping = false;
            _jumpHeld = false;
        }
    }
}
