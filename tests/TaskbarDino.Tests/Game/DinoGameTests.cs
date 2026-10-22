using TaskbarDino.Core.Game;

namespace TaskbarDino.Tests.Game;

public class DinoGameTests
{
    private const float Step = 1f / 60f;

    private static DinoGame NewGame(int seed = 1234, float dinoCenterX = 60f, float fieldWidth = 400f)
    {
        var game = new DinoGame(new Random(seed));
        game.Reset(dinoCenterX, fieldWidth);
        return game;
    }

    private static void Advance(DinoGame game, double seconds)
    {
        for (double t = 0; t < seconds; t += Step)
        {
            game.Update(Step);
        }
    }

    private static void RunUntilCrash(DinoGame game, int maxSteps = 1200)
    {
        for (int i = 0; i < maxSteps && game.State == GameState.Running; i++)
        {
            game.Update(Step);
        }

        Assert.Equal(GameState.Crashed, game.State);
    }

    /// <summary>A simple bot: hold-jump when an obstacle is just ahead; duck for mid-height birds.</summary>
    private static void PlayBot(DinoGame game, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            bool duck = false;
            if (!game.Trex.IsJumping)
            {
                foreach (Obstacle o in game.Obstacles)
                {
                    float gap = o.X - (game.Trex.X + GameConfig.DinoWidth);
                    bool midBird = o.Type == ObstacleType.Pterodactyl && o.Y + o.Height == GameConfig.BirdBottoms[1];
                    bool highBird = o.Type == ObstacleType.Pterodactyl && o.Y + o.Height == GameConfig.BirdBottoms[2];
                    if (gap is > -30f and < 24f && midBird)
                    {
                        duck = true;
                        break;
                    }

                    if (gap is > 0f and < 15f && !highBird && !midBird)
                    {
                        game.OnJumpDown();
                        break;
                    }
                }
            }

            game.OnDuck(duck);
            game.Update(Step);
        }
    }

    [Fact]
    public void NewGame_StartsRunning_NoWaiting()
    {
        DinoGame game = NewGame();
        Assert.Equal(GameState.Running, game.State);
        Assert.Equal(0, game.Score);
    }

    [Fact]
    public void JumpDown_WhenGrounded_MakesTheDinoJump()
    {
        DinoGame game = NewGame();
        game.OnJumpDown();
        Assert.True(game.Trex.IsJumping);
    }

    [Fact]
    public void StandingStill_CrashesWithoutScoring()
    {
        DinoGame game = NewGame();
        RunUntilCrash(game);
        Assert.Equal(0, game.Score);
        Assert.Equal(TrexPose.Dead, game.Trex.Pose);
    }

    [Fact]
    public void Crash_FreezesTheScene_UntilRestart()
    {
        DinoGame game = NewGame();
        RunUntilCrash(game);
        int obstacles = game.Obstacles.Count;
        float x = game.Obstacles[0].X;

        Advance(game, 5); // nothing moves on the game-over screen

        Assert.Equal(GameState.Crashed, game.State);
        Assert.Equal(obstacles, game.Obstacles.Count);
        Assert.Equal(x, game.Obstacles[0].X);
    }

    [Fact]
    public void Restart_IsLockedOut_BrieflyAfterCrash_ThenAccepted()
    {
        DinoGame game = NewGame();
        RunUntilCrash(game);

        game.Update(Step);
        game.OnJumpDown(); // too soon: ignored so the game-over screen is seen
        Assert.Equal(GameState.Crashed, game.State);

        Advance(game, GameConfig.RestartLockoutSeconds + 0.1);
        Assert.True(game.RestartReady);
        game.OnJumpDown();

        Assert.Equal(GameState.Running, game.State);
        Assert.Equal(0, game.Score);
        Assert.Empty(game.Obstacles);
        Assert.True(game.Trex.IsAlive);
    }

    [Fact]
    public void DodgingObstacles_IncrementsScore_AndKeepsHighScore()
    {
        DinoGame game = NewGame(seed: 7);
        PlayBot(game, 60 * 40);
        Assert.True(game.HighScore >= 3, $"a dodging player should have scored, got {game.HighScore}");

        // After a restart the score resets but the best is kept.
        RunUntilCrash(game, 60 * 60);
        int best = game.HighScore;
        Advance(game, 1);
        game.OnJumpDown();
        Assert.Equal(0, game.Score);
        Assert.Equal(best, game.HighScore);
    }

    [Fact]
    public void Duck_LowersTheCollisionBox_OnTheGroundOnly()
    {
        DinoGame game = NewGame();
        game.OnDuck(true);
        Assert.Equal(TrexPose.Ducking, game.Trex.Pose);
        Assert.Equal(GameConfig.DuckHeight, game.Trex.Bounds.Height);

        game.OnDuck(false);
        game.OnJumpDown();
        game.OnDuck(true); // mid-air: drops faster, but does not duck
        Assert.Equal(TrexPose.Jumping, game.Trex.Pose);
        Assert.Equal(GameConfig.DinoHeight, game.Trex.Bounds.Height);
    }

    [Fact]
    public void SeedHighScore_SetsAFloor()
    {
        DinoGame game = NewGame();
        game.SeedHighScore(500);
        Assert.Equal(500, game.HighScore);
    }

    [Fact]
    public void Update_ClampsHugeDelta_NoRunawaySteps()
    {
        DinoGame game = NewGame();
        game.Update(5f);
        Assert.True(game.Obstacles.Count <= 2, "a single huge delta over-advanced the game");
    }

    [Fact]
    public void Deterministic_ForAGivenSeed()
    {
        DinoGame a = NewGame(seed: 99);
        DinoGame b = NewGame(seed: 99);

        PlayBot(a, 600);
        PlayBot(b, 600);

        Assert.Equal(a.State, b.State);
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Obstacles.Count, b.Obstacles.Count);
        Assert.Equal(a.Clouds.Count, b.Clouds.Count);
    }

    [Fact]
    public void Decor_ScrollsAndRecycles()
    {
        DinoGame game = NewGame();
        Advance(game, 10);
        Assert.NotEmpty(game.Clouds);
        Assert.True(game.Clouds.Count <= GameConfig.MaxClouds);
        Assert.InRange(game.GroundOffset, 0f, GameConfig.GroundTileWidth);
        Assert.All(game.Clouds, c => Assert.InRange(c.Y, GameConfig.CloudMinY, GameConfig.CloudMaxY));
    }

    [Fact]
    public void FrameRateIndependent_SameResultAt30And120Hz()
    {
        DinoGame slow = NewGame(seed: 5);
        DinoGame fast = NewGame(seed: 5);
        for (int i = 0; i < 30 * 8; i++) slow.Update(1f / 30f);
        for (int i = 0; i < 120 * 8; i++) fast.Update(1f / 120f);

        Assert.Equal(slow.State, fast.State);
        Assert.Equal(slow.Obstacles.Count, fast.Obstacles.Count);
        if (slow.Obstacles.Count > 0)
        {
            Assert.Equal(slow.Obstacles[0].X, fast.Obstacles[0].X, precision: 2);
        }
    }
}
