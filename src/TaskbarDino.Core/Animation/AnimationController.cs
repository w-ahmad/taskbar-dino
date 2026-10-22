using TaskbarDino.Core.Dino;

namespace TaskbarDino.Core.Animation;

/// <summary>
/// Maps the dino's behaviour (a base <see cref="DinoState"/> plus transient <see cref="DinoReaction"/>s)
/// onto concrete frames. A reaction one-shot temporarily overrides the base posture and then yields
/// back to it; a new reaction replaces a running one.
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

    public DinoState State => _state;

    /// <summary>The frame to draw this instant.</summary>
    public AnimationFrame CurrentFrame => _reactionActive ? _reactionAnimator.CurrentFrame : _baseAnimator.CurrentFrame;

    public bool ReactionActive => _reactionActive;

    /// <summary>Seconds until the drawn frame changes (infinity when static).</summary>
    public float TimeUntilNextFrame => _reactionActive ? _reactionAnimator.TimeUntilNextFrame : _baseAnimator.TimeUntilNextFrame;

    /// <summary>Switches the sustained posture (no-op if unchanged). Movement states cancel any reaction.</summary>
    public void SetState(DinoState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        _baseAnimator.Play(ClipForState(state));
        if (state is DinoState.Walking or DinoState.Running or DinoState.Jumping or DinoState.Held)
        {
            _reactionActive = false;
        }
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

    /// <summary>Cancels a reaction in progress (e.g. the user picked the dino up).</summary>
    public void CancelReaction() => _reactionActive = false;

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
