using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Game.Runner;

/// <summary>
/// The player dino. It holds a fixed horizontal spot (its taskbar icon) and only moves vertically:
/// a variable-height hop where holding the jump key floats it higher and longer (weaker gravity while
/// rising), so clustered obstacles can be cleared. Pure logic on a fixed 60 Hz step.
/// </summary>
public sealed class Trex
{
    private bool _jumping;
    private bool _jumpHeld;
    private float _velocity;   // >0 rising, <0 falling (units/frame)
    private float _jumpOffset; // units above the ground line (0 = grounded)
    private bool _alive = true;

    /// <summary>Left edge of the dino in logical units (set to the taskbar-icon position).</summary>
    public float X { get; set; }

    /// <summary>How far the feet are above the ground line (0 grounded), for the renderer.</summary>
    public float JumpOffset => _jumpOffset;

    public bool IsJumping => _jumping;

    public bool IsAlive => _alive;

    public TrexPose Pose => !_alive ? TrexPose.Dead : _jumping ? TrexPose.Jumping : TrexPose.Running;

    /// <summary>Collision bounds in logical units.</summary>
    public RectF Bounds
    {
        get
        {
            float feet = GameConfig.GroundY - _jumpOffset;
            return new RectF(X, feet - GameConfig.DinoHeight, GameConfig.DinoWidth, GameConfig.DinoHeight);
        }
    }

    public void Reset()
    {
        _jumping = false;
        _jumpHeld = false;
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

    public void Kill() => _alive = false;

    public void Step()
    {
        if (!_jumping)
        {
            return;
        }

        _jumpOffset += _velocity;

        // Hard ceiling: a held jump floats up to MaxHoldHeight then falls, so jump height is capped
        // (residual velocity can't coast the dino past it).
        if (_jumpOffset >= GameConfig.MaxHoldHeight && _velocity > 0f)
        {
            _jumpOffset = GameConfig.MaxHoldHeight;
            _velocity = 0f;
        }

        // Holding the key while still rising (and below the cap) applies weaker gravity, so the dino
        // floats higher and stays airborne longer — enough to clear clustered obstacles.
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
