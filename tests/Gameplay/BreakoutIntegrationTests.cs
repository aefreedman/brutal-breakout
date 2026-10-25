using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation;
using CrushKit.Brutal.Draw2D;
using BrutalBreakout.Simulation;
using Xunit;

public class BreakoutIntegrationTests
{
    [Theory]
    [InlineData("startingBalls")] [InlineData("catchZoneHeightFraction")]
    [InlineData("maxBouncesWithoutBrick")] [InlineData("bricks")]
    public void EveryRuleIsRequired(string key)
    {
        var json = JsonNode.Parse(GameplayConfigurationTests.Valid)!;
        json["breakout"]!.AsObject().Remove(key);
        Reject(json);
    }

    [Theory]
    [InlineData("startingBalls", "0")] [InlineData("startingBalls", "null")]
    [InlineData("catchZoneHeightFraction", "0")] [InlineData("catchZoneHeightFraction", "1")]
    [InlineData("catchZoneHeightFraction", "0.1")] [InlineData("maxBouncesWithoutBrick", "-1")]
    [InlineData("bricks", "null")] [InlineData("bricks", "[]")] [InlineData("bricks", "[null]")]
    [InlineData("bricks", "[{\"x\":400,\"y\":200,\"width\":100}]")]
    [InlineData("bricks", "[{\"x\":400,\"y\":200,\"width\":100,\"height\":0}]")]
    [InlineData("bricks", "[{\"x\":0,\"y\":200,\"width\":100,\"height\":30}]")]
    [InlineData("bricks", "[{\"x\":400,\"y\":800,\"width\":100,\"height\":30}]")]
    public void InvalidRuleOrLayoutFailsPureLoad(string key, string value)
    {
        var json = JsonNode.Parse(GameplayConfigurationTests.Valid)!;
        json["breakout"]![key] = JsonNode.Parse(value);
        Reject(json);
    }

    [Theory]
    [InlineData("{\"extra\":1,")]
    [InlineData("{\"startingBalls\":3,")]
    [InlineData("{\"startingB\\u0061lls\":3,")]
    public void DuplicateAndUnknownRulesReject(string prefix)
    {
        using var files = new GameplayConfigurationTests.Files();
        var invalid = GameplayConfigurationTests.Valid.Replace("\"breakout\":{", "\"breakout\":" + prefix);
        Assert.Throws<System.IO.InvalidDataException>(() => GameplayConfiguration.Load(files.Put(invalid)));
    }

    [Theory]
    [InlineData("x")] [InlineData("y")] [InlineData("width")] [InlineData("height")]
    public void BrickFieldsRequiredAndDuplicatesReject(string key)
    {
        var json = JsonNode.Parse(GameplayConfigurationTests.Valid)!;
        json["breakout"]!["bricks"]![0]!.AsObject().Remove(key);
        Reject(json);
        using var files = new GameplayConfigurationTests.Files();
        var invalid = GameplayConfigurationTests.Valid.Replace("{\"x\":400", "{\"" + key + "\":1,\"x\":400");
        Assert.Throws<System.IO.InvalidDataException>(() => GameplayConfiguration.Load(files.Put(invalid)));
    }

    private static void Reject(JsonNode node)
    {
        using var files = new GameplayConfigurationTests.Files();
        Assert.Throws<System.IO.InvalidDataException>(() => GameplayConfiguration.Load(files.Put(node.ToJsonString())));
    }

    private static GameplaySettings Game(int balls = 2, bool oneBrick = false, bool downward = false) => new(
        new(1000, 900, 50, 50, 950, 850, 20), new(10, 600, 500, 750, 0, downward ? 1 : -1),
        new(balls, 1.0 / 3, 12, oneBrick ? [new(450, 200, 100, 30)] : [new(450, 200, 100, 30), new(100, 120, 80, 25)]));

    private static void Until(BallSandboxSession session, ArenaViewport viewport, Func<bool> condition)
    {
        for (int i = 0; i < 2500 && !condition(); i++)
            session.Advance(new InputSnapshot { Focused = true }, session.Settings.StepSeconds, viewport);
        Assert.True(condition());
    }

    [Theory]
    [InlineData(60)] [InlineData(120)] [InlineData(240)]
    public void RealHostCatchesBelowLineThenMissesAndRestartsWithoutRestoringBricksOnMiss(int hz)
    {
        var game = Game();
        using var host = new BallSandboxSession(new(hz, 4, 8, 120, 1), game, TestSettings.Input);
        var viewport = new ArenaViewport(1000, 900, 2000, 1800, game.Arena);
        var idle = new InputSnapshot { Focused = true };
        host.Advance(idle, 0, viewport);
        host.Advance(idle with { KeyboardPrimary = true }, host.Settings.StepSeconds, viewport);
        Until(host, viewport, () => host.Simulation.State.Position.Y < host.Simulation.CatchLineY);
        host.Advance(idle with { KeyboardPrimary = true }, host.Settings.StepSeconds, viewport);
        Assert.Equal(BallPhase.Flying, host.Simulation.State.Phase);
        Until(host, viewport, () => host.Simulation.State.CatchEligible);
        Assert.Equal(1, host.Simulation.RemainingBricks);
        var caught = host.Simulation.State.Position;
        host.Advance(idle with { KeyboardPrimary = true }, host.Settings.StepSeconds, viewport);
        Assert.Equal(caught, host.Simulation.State.Position);
        Assert.Equal(BallPhase.Aiming, host.Simulation.State.Phase);
        host.Advance(idle with { KeyboardDirection = new(0, -1) }, host.Settings.StepSeconds, viewport);
        host.Advance(idle with { KeyboardPrimary = true, KeyboardDirection = new(0, -1) }, host.Settings.StepSeconds, viewport);
        Until(host, viewport, () => host.Simulation.BallsRemaining == 1);
        Assert.Equal(1, host.Simulation.RemainingBricks);
        Assert.Equal(game.Ball.Spawn, host.Simulation.State.Position);
        host.Advance(idle with { KeyboardRestart = true }, host.Settings.StepSeconds, viewport);
        Assert.Equal(2, host.Simulation.RemainingBricks);
        Assert.Equal(2, host.Simulation.BallsRemaining);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TerminalSceneHasRealStatusGeometryAndRestarts(bool lose)
    {
        var game = Game(1, oneBrick: true, downward: lose);
        using var host = new BallSandboxSession(TestSettings.Baseline, game, TestSettings.Input);
        var viewport = new ArenaViewport(1000, 900, 1000, 900, game.Arena);
        var idle = new InputSnapshot { Focused = true };
        host.Advance(idle, 0, viewport);
        host.Advance(idle with { KeyboardPrimary = true }, host.Settings.StepSeconds, viewport);
        Until(host, viewport, () => host.Simulation.RoundPhase != RoundPhase.Playing);
        Assert.Equal(lose ? RoundPhase.Lost : RoundPhase.Won, host.Simulation.RoundPhase);
        using var draw = new ShapeDrawList();
        var frame = BallSandbox.Build(draw, host.Simulation, TestSettings.Colors, TestSettings.Style, 0, 1000, 900, out var evidence);
        Assert.Equal(0, evidence.HeldRingIndices); Assert.Equal(0, evidence.BallIndices); Assert.Equal(0, evidence.AimIndices);
        Assert.True(evidence.HudIndices > 0 && frame.Indices.Length > 0);
        host.Advance(idle with { KeyboardRestart = true }, host.Settings.StepSeconds, viewport);
        Assert.Equal(RoundPhase.Playing, host.Simulation.RoundPhase);
        Assert.Equal(1, host.Simulation.RemainingBricks);
    }

    [Fact]
    public void CategoryEvidenceUsesActualIndexRangesEvenWhenEveryColorIsIdentical()
    {
        var c = new LinearColor(.5f, .5f, .5f);
        var same = new ColorScheme("same", c, c, c, c, c, c, c, c, c, c, c, c, c, c);
        using var sim = new BallSimulation(TestSettings.Baseline, Game());
        using var draw = new ShapeDrawList();
        var frame = BallSandbox.Build(draw, sim, same, TestSettings.Style, 0, 1000, 900, out var e);
        Assert.Equal(18, e.ArenaIndices);
        Assert.True(e.BrickIndices > 0 && e.CatchLineIndices > 0 && e.HudIndices > 0 && e.BallIndices > 0 && e.AimIndices > 0);
        Assert.Equal(frame.Indices.Length, e.ArenaIndices + e.BrickIndices + e.CatchLineIndices + e.HudIndices + e.BallIndices + e.AimIndices + e.BackgroundIndices + e.HeldRingIndices);
        foreach (var v in frame.Vertices.Span) Assert.Equal(c.R, v.Color.X);
    }
}
