using System.Diagnostics.CodeAnalysis;

using TaskbarDino.Core.Graphics;

namespace TaskbarDino.Animation;

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

    public AtlasRegion CurrentRegion => _clip.Frames[_frameIndex].Region;

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

            if (_frameIndex < _clip.Frames.Count)
            {
                continue;
            }

            if (_clip.Loop)
            {
                _frameIndex = 0;
            }
            else
            {
                _frameIndex = _clip.Frames.Count - 1;
                Finished = true;
                break;
            }
        }
    }
}
