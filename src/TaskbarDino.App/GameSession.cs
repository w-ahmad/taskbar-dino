using Microsoft.Extensions.Logging;

using TaskbarDino.App.Content;
using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Platform;
using TaskbarDino.Core.Primitives;
using TaskbarDino.Core.Settings;
using TaskbarDino.Core.Time;
using TaskbarDino.Game.Runner;

namespace TaskbarDino.App;

/// <summary>
/// Drives Game Mode over a transparent stage above the taskbar: the dino (drawn from the themed
/// companion atlas) holds its icon position and hops to clear obstacles that scroll in from the right.
/// Nothing else is drawn but the dino, the obstacles and a small score cloud. The
/// <see cref="CompanionRuntime"/> sizes/positions the overlay and hands the renderer + companion atlas
/// over via <see cref="Enter"/>, then delegates each frame to <see cref="Tick"/>; the session raises
/// <see cref="Exited"/> on Esc. All drawing is in surface-local pixels — logical game units × scale.
/// </summary>
public sealed class GameSession(
    IGameInput gameInput,
    ISettingsService settings,
    ILogger<GameSession> logger)
{
    private const string DinoIdle = "idle";
    private const string DinoJump = "jump";
    private const string DinoWalk1 = "walk1";
    private const string DinoWalk2 = "walk2";

    private readonly IGameInput _gameInput = gameInput;
    private readonly ISettingsService _settings = settings;
    private readonly ILogger<GameSession> _logger = logger;
    private readonly DinoGame _game = new();

    private IRenderer? _renderer;
    private SpriteAtlas? _companion;
    private TextureHandle _obstacleTexture;
    private bool _textureReady;

    private float _scale = 1f;
    private float _dinoCenterX;   // logical
    private float _fieldWidth;    // logical
    private bool _wasCrashed;

    public bool Active { get; private set; }

    /// <summary>Raised (on the loop thread) when the player leaves the game with Esc.</summary>
    public event EventHandler? Exited;

    /// <summary>Enters Game Mode. The surface is already resized to the full-width stage above the taskbar.</summary>
    public void Enter(IRenderer renderer, SpriteAtlas companion, float dinoCenterX, float fieldWidth, float scale)
    {
        _renderer = renderer;
        _companion = companion;
        _scale = MathF.Max(1f, scale);
        _dinoCenterX = dinoCenterX;
        _fieldWidth = fieldWidth;

        if (!_textureReady)
        {
            _obstacleTexture = renderer.CreateTexture(GameSpriteSheet.CreatePixels(), GameSpriteSheet.AtlasWidth, GameSpriteSheet.AtlasHeight);
            _textureReady = true;
        }

        _game.SeedHighScore(_settings.Current.HighScore);
        _game.Reset(dinoCenterX, fieldWidth);
        _wasCrashed = false;

        _gameInput.JumpChanged += OnJumpChanged;
        _gameInput.ExitPressed += OnExit;
        _gameInput.Enable();

        Active = true;
        _logger.LogInformation("Game Mode entered (HI {HighScore}).", _game.HighScore);
    }

    /// <summary>Leaves Game Mode, stops input and persists the high score.</summary>
    public void Leave()
    {
        if (!Active)
        {
            return;
        }

        _gameInput.Disable();
        _gameInput.JumpChanged -= OnJumpChanged;
        _gameInput.ExitPressed -= OnExit;
        Active = false;
        PersistHighScore();
        _logger.LogInformation("Game Mode left (best {HighScore}).", _game.HighScore);
    }

    public FrameOutcome Tick(FrameTime time)
    {
        _game.Update(time.DeltaSeconds);

        if (_game.State == GameState.Crashed && !_wasCrashed)
        {
            _wasCrashed = true;
            PersistHighScore();
        }
        else if (_game.State == GameState.Running)
        {
            _wasCrashed = false;
        }

        Draw();
        return FrameOutcome.Continuous; // an endless runner — always animating
    }

    private void OnJumpChanged(object? sender, bool down)
    {
        if (down)
        {
            _game.OnJumpDown();
        }
        else
        {
            _game.OnJumpUp();
        }
    }

    private void OnExit(object? sender, EventArgs e) => Exited?.Invoke(this, EventArgs.Empty);

    private void PersistHighScore()
    {
        if (_game.HighScore > _settings.Current.HighScore)
        {
            _settings.Update(s => s with { HighScore = _game.HighScore });
        }
    }

    private void Draw()
    {
        if (_renderer is null || _companion is null)
        {
            return;
        }

        _renderer.BeginFrame(); // clears to transparent — no background panel

        foreach (Obstacle obstacle in _game.Obstacles)
        {
            DrawObstacle(obstacle);
        }

        DrawDino();
        DrawScoreCloud();

        _renderer.EndFrame();
    }

    private void DrawDino()
    {
        string frame = _game.Trex.Pose switch
        {
            TrexPose.Jumping => DinoJump,
            TrexPose.Dead => DinoIdle,
            _ => _game.RunFrame == 0 ? DinoWalk1 : DinoWalk2,
        };

        float w = GameConfig.DinoWidth * _scale;
        float h = GameConfig.DinoHeight * _scale;
        float centerX = _dinoCenterX * _scale;
        float feetY = (GameConfig.GroundY - _game.Trex.JumpOffset) * _scale;
        _renderer!.DrawSprite(_companion!.Texture, _companion[frame], new RectF(centerX - (w / 2f), feetY - h, w, h), Rgba.White);
    }

    private void DrawObstacle(Obstacle obstacle)
    {
        if (obstacle.Type == ObstacleType.Pterodactyl)
        {
            DrawGame(_game.WingFrame == 0 ? GameSpriteSheet.Ptero1 : GameSpriteSheet.Ptero2, obstacle.X, obstacle.Y);
            return;
        }

        string name = obstacle.Type == ObstacleType.CactusLarge ? GameSpriteSheet.CactusLarge : GameSpriteSheet.CactusSmall;
        for (int i = 0; i < obstacle.Count; i++)
        {
            DrawGame(name, obstacle.X + (i * obstacle.Width), obstacle.Y);
        }
    }

    private void DrawScoreCloud()
    {
        PixelRect cloud = GameSpriteSheet.Regions[GameSpriteSheet.Cloud].Source;
        float cloudLeft = Math.Clamp(_dinoCenterX - (cloud.Width / 2f), 0f, MathF.Max(0f, _fieldWidth - cloud.Width));

        // Sit just above the dino's head and follow it up when it jumps, so it always hugs the dino.
        float headY = GameConfig.GroundY - _game.Trex.JumpOffset - GameConfig.DinoHeight;
        float cloudTop = MathF.Max(0f, headY - 3f - cloud.Height);
        DrawGame(GameSpriteSheet.Cloud, cloudLeft, cloudTop);

        string score = _game.Score.ToString("D3");
        float glyphScale = Math.Max(1, (int)MathF.Round(_scale * 0.9f));
        float advance = (GameSpriteSheet.GlyphWidth + 1) * glyphScale;
        float textWidth = (score.Length * advance) - glyphScale;
        float cloudCenterX = (cloudLeft + (cloud.Width / 2f)) * _scale;
        float cloudCenterY = (cloudTop + (cloud.Height / 2f)) * _scale;
        float x = cloudCenterX - (textWidth / 2f);
        float y = cloudCenterY - (GameSpriteSheet.GlyphHeight * glyphScale / 2f);

        foreach (char c in score)
        {
            string glyph = GameSpriteSheet.Glyph(c);
            if (GameSpriteSheet.Regions.ContainsKey(glyph))
            {
                _renderer!.DrawSprite(_obstacleTexture, GameSpriteSheet.Regions[glyph], new RectF(x, y, GameSpriteSheet.GlyphWidth * glyphScale, GameSpriteSheet.GlyphHeight * glyphScale), Rgba.White);
            }

            x += advance;
        }
    }

    private void DrawGame(string region, float logicalX, float logicalY)
    {
        PixelRect src = GameSpriteSheet.Regions[region].Source;
        _renderer!.DrawSprite(_obstacleTexture, GameSpriteSheet.Regions[region], new RectF(logicalX * _scale, logicalY * _scale, src.Width * _scale, src.Height * _scale), Rgba.White);
    }
}
