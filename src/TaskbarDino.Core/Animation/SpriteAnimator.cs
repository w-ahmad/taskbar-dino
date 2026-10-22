using System.Diagnostics.CodeAnalysis;

namespace TaskbarDino.Core.Animation;

/// <summary>
/// Plays a single <see cref="AnimationClip"/>, advancing the current frame by elapsed time.
/// A non-looping clip latches <see cref="Finished"/> on its last frame; a looping clip wraps.
/// </summary>
public sealed class SpriteAnimator
{
    private AnimationClip _clip;
    private int _frameIndex;
    private float _frameElapsed;

    public SpriteAnimator(AnimationClip clip) => Play(clip);

    public AnimationClip Clip => _clip;

    public bool Finished { get; private set; }

    public AnimationFrame CurrentFrame => _clip.Frames[_frameIndex];

    /// <summary>
    /// Seconds until the displayed frame changes, or <see cref="float.PositiveInfinity"/> when it never
    /// will (static pose or finished one-shot). Lets the loop sleep exactly until the next visual change.
    /// </summary>
    public float TimeUntilNextFrame
    {
        get
        {
            if (Finished || !_clip.IsAnimated)
            {
                return float.PositiveInfinity;
            }

            return MathF.Max(0f, _clip.Frames[_frameIndex].DurationSeconds - _frameElapsed);
        }
    }

    [MemberNotNull(nameof(_clip))]
    public void Play(AnimationClip clip)
    {
        _clip = clip;
        _frameIndex = 0;
        _frameElapsed = 0f;
        Finished = false;
    }

    public void Update(float deltaSeconds)
    {
        if (Finished)
        {
            return;
        }

        _frameElapsed += deltaSeconds;

        while (_frameElapsed >= _clip.Frames[_frameIndex].DurationSeconds)
        {
            _frameElapsed -= _clip.Frames[_frameIndex].DurationSeconds;
            _frameIndex++;

            if (_frameIndex < _clip.Frames.Length)
            {
                continue;
            }

            if (_clip.Loop)
            {
                _frameIndex = 0;
            }
            else
            {
                _frameIndex = _clip.Frames.Length - 1;
                Finished = true;
                break;
            }
        }
    }
}
