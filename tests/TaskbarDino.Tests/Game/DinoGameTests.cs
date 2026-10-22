using TaskbarDino.Game.Runner;

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

        for (int i = 0; i < 600 && game.State == GameState.Running; i++)
        {
            game.Update(Step);
        }

        // It hit the first obstacle having dodged none — the score is dodges, not distance.
        Assert.Equal(GameState.Crashed, game.State);
        Assert.Equal(0, game.Score);
    }

    [Fact]
    public void DodgingObstacles_IncrementsScore()
    {
        DinoGame game = NewGame(seed: 7);

        // A simple player: hold-jump whenever an obstacle is approaching just ahead and we're grounded.
        for (int i = 0; i < 1800; i++) // 30 s
        {
            if (!game.Trex.IsJumping)
            {
                foreach (Obstacle o in game.Obstacles)
                {
                    float gap = o.X - (game.Trex.X + GameConfig.DinoWidth);
                    if (gap is > 0f and < 15f)
                    {
                        game.OnJumpDown();
                        break;
                    }
                }
            }

            game.Update(Step);
        }

        Assert.True(game.HighScore >= 1, $"a dodging player should have scored at least one, got {game.HighScore}");
    }

    [Fact]
    public void SeedHighScore_SetsAFloor()
    {
        DinoGame game = NewGame();
        game.SeedHighScore(500);

        Assert.Equal(500, game.HighScore);
    }

    [Fact]
    public void StandingStill_Crashes_ThenAutoRestarts()
    {
        DinoGame game = NewGame();

        bool crashed = false;
        for (int i = 0; i < 600 && !crashed; i++) // up to 10 s
        {
            game.Update(Step);
            crashed = game.State == GameState.Crashed;
        }

        Assert.True(crashed, "the stationary dino never hit an obstacle");

        bool ranAgain = false;
        for (int i = 0; i < 120 && !ranAgain; i++) // within ~2 s the crash pause ends
        {
            game.Update(Step);
            ranAgain = game.State == GameState.Running;
        }

        Assert.True(ranAgain, "the run did not auto-restart after the crash");
    }

    [Fact]
    public void Update_ClampsHugeDelta_NoRunawaySteps()
    {
        DinoGame game = NewGame();

        game.Update(5f); // a massive stall must not fast-forward the whole world

        Assert.True(game.Score < 50, $"a single huge delta over-advanced the game (score {game.Score})");
    }

    [Fact]
    public void Deterministic_ForAGivenSeed()
    {
        DinoGame a = NewGame(seed: 99);
        DinoGame b = NewGame(seed: 99);

        Advance(a, 3.0);
        Advance(b, 3.0);

        Assert.Equal(a.State, b.State);
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Obstacles.Count, b.Obstacles.Count);
    }
}
