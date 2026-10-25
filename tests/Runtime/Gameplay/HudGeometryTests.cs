using System.Text.Json.Nodes;
using Brutal.Numerics;
using BrutalBreakout.Configuration;
using BrutalBreakout.Presentation;
using CrushKit.Brutal.Draw2D;
using BrutalBreakout.Simulation;
using Xunit;

public class HudGeometryTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void CustomHudControlsActualBarAndTerminalIndexedVertices(bool lose, bool fractional)
    {
        using var files = new GameplayConfigurationTests.Files();
        var json = JsonNode.Parse(PresentationStyleConfigurationTests.Valid)!;
        json["hud"] = JsonNode.Parse(fractional ? """
            {"barMaxWidth":300,"barMaxArenaWidthFraction":0.1,"barHeight":10,"barOutlineWidth":2,
             "barMinCenterY":3,"barTopMarginFraction":0.6,"terminalMaxSize":100,
             "terminalMaxArenaSizeFraction":0.04,"terminalMarkerWidth":7}
            """ : """
            {"barMaxWidth":80,"barMaxArenaWidthFraction":0.3,"barHeight":12,"barOutlineWidth":3,
             "barMinCenterY":25,"barTopMarginFraction":0.2,"terminalMaxSize":35,
             "terminalMaxArenaSizeFraction":0.2,"terminalMarkerWidth":5}
            """);
        var style = PresentationStyleConfiguration.Load(files.Put(json.ToJsonString())).Settings;
        var game = new GameplaySettings(new(1000, 900, 50, 50, 950, 850, 20),
            new(10, 600, 500, 750, 0, lose ? 1 : -1), new(1, 1.0 / 3, 12, [new(450, 200, 100, 30)]));
        style.Validate(game);
        using var sim = new BallSimulation(TestSettings.Baseline, game, new(2, 64, 2));
        sim.Step(new(PrimaryPressed: true));
        for (int i = 0; i < 2000 && sim.RoundPhase == RoundPhase.Playing; i++) sim.Step();
        Assert.Equal(lose ? RoundPhase.Lost : RoundPhase.Won, sim.RoundPhase);
        using var draw = new ShapeDrawList();
        var frame = BallSandbox.Build(draw, sim, TestSettings.Colors, style, 0, 1000, 900, out var e);
        float bar = fractional ? 90 : 80, y = fractional ? 18 : 25;
        float height = fractional ? 10 : 12, outline = fractional ? 2 : 3;
        float size = fractional ? 32 : 35, marker = fractional ? 7 : 5;
        using var expected = new ShapeDrawList(); expected.BeginFrame(1000, 900);
        var c = TestSettings.Colors;
        expected.StrokeRect(new(50, y - height / 2), new(bar, height), outline, c.Hud);
        expected.FillRect(new(50, y - height / 2), new(bar * sim.BallsRemaining, height), c.Hud);
        expected.StrokeRect(new(950 - bar, y - height / 2), new(bar, height), outline, c.Hud);
        expected.FillRect(new(950 - bar, y - height / 2), new(bar * sim.RemainingBricks, height), c.Hud);
        float cy = (float)game.CatchLineY;
        if (!lose)
            expected.StrokePolyline([new(500 - size, cy), new(500 - size / 4, cy + size / 2), new(500 + size, cy - size)], marker, c.Win);
        else
        {
            expected.StrokePolyline([new(500 - size, cy - size), new(500 + size, cy + size)], marker, c.Loss);
            expected.StrokePolyline([new(500 - size, cy + size), new(500 + size, cy - size)], marker, c.Loss);
        }
        var reference = expected.Finish();
        var actualVertices = frame.Indices.Slice(frame.Indices.Length - e.HudIndices).ToArray().Select(i => frame.Vertices.Span[(int)i]).ToArray();
        var expectedVertices = reference.Indices.ToArray().Select(i => reference.Vertices.Span[(int)i]).ToArray();
        Assert.Equal(expectedVertices, actualVertices);
        Assert.Equal(bar + outline, e.HudBarOuterWidth, 3);
        Assert.Equal(height + outline, e.HudBarOuterHeight, 3);
        Assert.Equal(y, e.HudBarCenterY, 3);
    }
}
