using TaskbarDino.Core.Dino;
using TaskbarDino.Core.Graphics;

namespace TaskbarDino.Animation;

/// <summary>
/// Maps the dino's behaviour (a base <see cref="DinoState"/> plus transient <see cref="DinoReaction"/>s)
/// onto concrete animation frames, centralising all "which frame is on screen right now" logic.
/// A reaction one-shot temporarily overrides the base posture and then yields back to it.
/// </summary>
public sealed class AnimationController
{
    private readonly IReadOnlyDictionary<DinoState, AnimationClip> _stateClips;
    private readonly IReadOnlyDictionary<DinoReaction, AnimationClip> _reactionClips;

    private readonly SpriteAnimator _baseAnimator;
    private readonly SpriteAnimator _reactionAnimator;

    private DinoState _state;
    private bool _reactionActive;

    public AnimationController(
        IReadOnlyDictionary<DinoState, AnimationClip> stateClips,
        IReadOnlyDictionary<DinoReaction, AnimationClip> reactionClips,
        DinoState initialState)
    {
        _stateClips = stateClips;
        _reactionClips = reactionClips;
        _state = initialState;
        _baseAnimator = new SpriteAnimator(ClipForState(initialState));
        _reactionAnimator = new SpriteAnimator(ClipForState(initialState));
    }

    /// <summary>The frame to draw this instant.</summary>
    public AtlasRegion CurrentRegion => _reactionActive ? _reactionAnimator.CurrentRegion : _baseAnimator.CurrentRegion;

    /// <summary>True while a reaction one-shot or an animated base clip is in progress.</summary>
    public bool IsAnimating => _reactionActive || _baseAnimator.Clip.IsAnimated;

    /// <summary>Switches the sustained posture (no-op if unchanged).</summary>
    public void SetState(DinoState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        _baseAnimator.Play(ClipForState(state));
    }

    /// <summary>Starts a one-shot reaction that overrides the base posture until it finishes.</summary>
    public void Trigger(DinoReaction reaction)
    {
        if (!_reactionClips.TryGetValue(reaction, out AnimationClip? clip))
        {
            return;
        }

        _reactionAnimator.Play(clip);
        _reactionActive = true;
    }

    public void Update(float deltaSeconds)
    {
        _baseAnimator.Update(deltaSeconds);

        if (!_reactionActive)
        {
            return;
        }

        _reactionAnimator.Update(deltaSeconds);
        if (_reactionAnimator.Finished)
        {
            _reactionActive = false;
        }
    }

    private AnimationClip ClipForState(DinoState state) =>
        _stateClips.TryGetValue(state, out AnimationClip? clip)
            ? clip
            : _stateClips[DinoState.Idle];
}
