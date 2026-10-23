using TaskbarDino.Core.Content;
using TaskbarDino.Core.Game;
using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;
using TaskbarDino.Core.Time;
using TaskbarDino.Rendering;

namespace TaskbarDino;

/// <summary>
/// Draws and advances Game Mode over the transparent strip above the taskbar: scrolling ground,
/// drifting clouds, obstacles, the dino (from the themed companion sheet), a score bubble with the
/// best score, and the game-over screen. All drawing is in strip-local pixels (logical units ×
/// scale). The runtime owns the window and input; this class owns the game and its picture.
/// Rendering stops entirely on the game-over screen (nothing moves until a key arrives).
/// </summary>
internal sealed class GameSession
{
    private readonly DinoGame _game;
    private readonly List<float> _tileXs = new(64);

    private Renderer _renderer = null!;
    private Texture _dino = null!;
    private Texture _art = null!;
    private SpriteSheet _dinoSheet = null!;
    private SpriteSheet _artSheet = null!;

    private float _scale = 1f;
    private float _dinoCenterX; // logical, game space
    private float _fieldWidth;  // logical
    private bool _mirrored;     // obstacles enter from the left on screen
    private bool _gameOverDrawn;
    private bool _restartHintDrawn;

    public GameSession(Random rng) => _game = new DinoGame(rng);

    public int HighScore => _game.HighScore;

    public bool IsCrashed => _game.State == GameState.Crashed;

    public void Enter(Renderer renderer, Texture dino, SpriteSheet dinoSheet, Texture art, SpriteSheet artSheet, float dinoCenterX, float fieldWidth, float scale, int highScore)
    {
        _renderer = renderer;
        _dino = dino;
        _dinoSheet = dinoSheet;
        _art = art;
        _artSheet = artSheet;
        _scale = MathF.Max(1f, scale);
        _fieldWidth = fieldWidth;
        _mirrored = DinoGame.ShouldMirror(dinoCenterX, fieldWidth);
        _dinoCenterX = _mirrored ? fieldWidth - dinoCenterX : dinoCenterX;
        _gameOverDrawn = false;
        _game.SeedHighScore(highScore);
        _game.Reset(_dinoCenterX, fieldWidth);
    }

    /// <summary>The renderer was recreated after a device loss: pick up the new textures.</summary>
    public void Rebind(Renderer renderer, Texture dino, Texture art)
    {
        _renderer = renderer;
        _dino = dino;
        _art = art;
        _gameOverDrawn = false;
    }

    public void OnJump(bool down)
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

    public void OnDuck(bool down) => _game.OnDuck(down);

    public FrameOutcome Tick(FrameTime time)
    {
        _game.Update(time.DeltaSeconds);

        if (_game.State == GameState.Crashed)
        {
            // Frozen scene: draw it once at the crash and once more when the restart hint appears, then
            // sleep until input arrives — the game-over screen costs nothing while it waits.
            if (_gameOverDrawn && (_restartHintDrawn || !_game.RestartReady))
            {
                return _game.RestartReady ? FrameOutcome.Idle : FrameOutcome.WakeIn(TimeSpan.FromSeconds(GameConfig.RestartLockoutSeconds));
            }

            _gameOverDrawn = true;
            _restartHintDrawn = _game.RestartReady;
            Draw();
            return _game.RestartReady ? FrameOutcome.Idle : FrameOutcome.WakeIn(TimeSpan.FromSeconds(GameConfig.RestartLockoutSeconds));
        }

        _gameOverDrawn = false;
        _restartHintDrawn = false;
        Draw();
        return FrameOutcome.Continuous;
    }

    private void Draw()
    {
        _renderer.Begin();
        DrawGround();
        foreach (Cloud cloud in _game.Clouds)
        {
            DrawArt(GameSpriteSheet.Cloud, cloud.X, cloud.Y);
        }

        foreach (Obstacle obstacle in _game.Obstacles)
        {
            DrawObstacle(obstacle);
        }

        DrawDino();
        DrawScoreBubble();
        if (_game.State == GameState.Crashed)
        {
            DrawGameOver();
        }

        _renderer.End();
    }

    private void DrawGround()
    {
        AtlasRegion tile = _artSheet[GameSpriteSheet.Ground];
        float tileW = GameConfig.GroundTileWidth;
        float top = GameConfig.GroundY - 3f; // the line sits just under the feet; pebbles fill the last rows
        float offset = _mirrored ? tileW - _game.GroundOffset : _game.GroundOffset;
        for (float x = -offset; x < _fieldWidth; x += tileW)
        {
            _renderer.Draw(_art, tile, new RectF(x * _scale, (top - 1f) * _scale, tile.Width * _scale, tile.Height * _scale), _mirrored);
        }
    }

    private void DrawDino()
    {
        string frame = _game.Trex.Pose switch
        {
            TrexPose.Jumping => DinoSpriteSheet.Jump,
            TrexPose.Dead => DinoSpriteSheet.Dead,
            TrexPose.Ducking => _game.RunFrame == 0 ? DinoSpriteSheet.Duck1 : DinoSpriteSheet.Duck2,
            _ => _game.RunFrame == 0 ? DinoSpriteSheet.Walk1 : DinoSpriteSheet.Walk2,
        };

        float w = GameConfig.DinoWidth * _scale;
        float h = GameConfig.DinoHeight * _scale;
        float centerX = DinoScreenCenter() * _scale;
        float feetY = (GameConfig.GroundY - _game.Trex.JumpOffset) * _scale;
        _renderer.Draw(_dino, _dinoSheet[frame], new RectF(centerX - (w / 2f), feetY - h, w, h), _mirrored);
    }

    private void DrawObstacle(Obstacle obstacle)
    {
        if (obstacle.Type == ObstacleType.Pterodactyl)
        {
            DrawArt(_game.WingFrame == 0 ? GameSpriteSheet.Ptero1 : GameSpriteSheet.Ptero2, obstacle.X, obstacle.Y);
            return;
        }

        string name = obstacle.Type == ObstacleType.CactusLarge ? GameSpriteSheet.CactusLarge : GameSpriteSheet.CactusSmall;
        for (int i = 0; i < obstacle.Count; i++)
        {
            DrawArt(name, obstacle.X + (i * obstacle.Width), obstacle.Y);
        }
    }

    private void DrawScoreBubble()
    {
        AtlasRegion bubble = _artSheet[GameSpriteSheet.Bubble];
        float left = Math.Clamp(DinoScreenCenter() - (bubble.Width / 2f), 0f, MathF.Max(0f, _fieldWidth - bubble.Width));

        // Just above the dino's head, following it up when it jumps.
        float headY = GameConfig.GroundY - _game.Trex.JumpOffset - GameConfig.DinoHeight;
        float top = MathF.Max(0f, headY - 3f - bubble.Height);
        DrawArt(GameSpriteSheet.Bubble, left, top, mirrorPosition: false);

        int glyph = Math.Max(1, (int)MathF.Round(_scale * 0.9f)); // integer glyph scale keeps the 1px font gaps
        float lineH = GameSpriteSheet.GlyphHeight * glyph;
        float cx = (left + (bubble.Width / 2f)) * _scale;
        float cy = (top + (bubble.Height / 2f)) * _scale;
        DrawText($"HI {_game.HighScore:D4}", cx, cy - lineH - (glyph * 0.5f), glyph);
        DrawText(_game.Score.ToString("D4"), cx, cy + (glyph * 1f), glyph);
    }

    private void DrawGameOver()
    {
        int glyph = Math.Max(1, (int)MathF.Round(_scale * 1.3f));
        float cx = DinoScreenCenter() * _scale;
        float y = 4f * _scale;
        float textW = DrawText("GAME OVER", cx, y, glyph);
        AtlasRegion restart = _artSheet[GameSpriteSheet.Restart];
        if (_game.RestartReady)
        {
            float rx = cx + (textW / 2f) + (6f * _scale);
            float ry = y - (((restart.Height * _scale) - (GameSpriteSheet.GlyphHeight * glyph)) / 2f);
            _renderer.Draw(_art, restart, new RectF(rx, ry, restart.Width * _scale, restart.Height * _scale));
        }
    }

    /// <summary>Draws centred text with the 5×7 font; returns its width in pixels.</summary>
    private float DrawText(string text, float centerX, float top, int glyph)
    {
        float advance = (GameSpriteSheet.GlyphWidth + 1) * glyph;
        float width = (text.Length * advance) - glyph;
        float x = centerX - (width / 2f);
        foreach (char c in text)
        {
            if (c != ' ' && _artSheet.Regions.TryGetValue(GameSpriteSheet.Glyph(c), out AtlasRegion region))
            {
                // Glyph cells carry a 1px outline border; draw the inner 5×7 so spacing stays exact.
                var inner = new AtlasRegion(new PixelRect(region.Source.X + 1, region.Source.Y + 1, GameSpriteSheet.GlyphWidth, GameSpriteSheet.GlyphHeight));
                _renderer.Draw(_art, inner, new RectF(x, top, GameSpriteSheet.GlyphWidth * glyph, GameSpriteSheet.GlyphHeight * glyph));
            }

            x += advance;
        }

        return width;
    }

    /// <summary>Maps a game-space X (left edge of something <paramref name="width"/> wide) to screen space.</summary>
    private float ScreenX(float gameX, float width) => _mirrored ? _fieldWidth - gameX - width : gameX;

    private float DinoScreenCenter() => ScreenX(_dinoCenterX - (GameConfig.DinoWidth / 2f), GameConfig.DinoWidth) + (GameConfig.DinoWidth / 2f);

    private void DrawArt(string name, float logicalX, float logicalY, bool mirrorPosition = true)
    {
        AtlasRegion region = _artSheet[name];
        // Art cells include a 1px outline border, so offset by one unit to keep the body where the game thinks it is.
        float bodyWidth = region.Width - 2f;
        float x = mirrorPosition ? ScreenX(logicalX, bodyWidth) : logicalX;
        _renderer.Draw(_art, region, new RectF((x - 1f) * _scale, (logicalY - 1f) * _scale, region.Width * _scale, region.Height * _scale), _mirrored && mirrorPosition);
    }
}
