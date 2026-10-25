using System.Collections.ObjectModel;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class BreakoutSettingsTests
{
    private static GameplaySettings Validate(BreakoutSettings rules, BallSettings? ball = null) =>
        new(BreakoutTests.Game().Arena, ball ?? BreakoutTests.Game().Ball, rules);
    private static BreakoutSettings Rules(IReadOnlyList<BrickSettings> bricks) => new(3, 1.0 / 3, 12, bricks);

    [Fact]
    public void SettingsCopyLayoutAndExposeOnlyReadOnlyValues()
    {
        BrickSettings[] source = [new(450, 200, 100, 30)];
        var original = source[0];
        var rules = Rules(source);
        source[0] = new(1, 2, 3, 4);
        Assert.Same(original, rules.Bricks[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<BrickSettings>)rules.Bricks)[0] = source[0]);
        Assert.Equal(3, rules.StartingBalls);
        Assert.Equal(12, rules.MaxBouncesWithoutBrick);
        Assert.Equal(1.0 / 3, rules.CatchZoneHeightFraction);
        foreach (var type in new[] { typeof(BrickSettings), typeof(BreakoutSettings) })
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        var game = Validate(rules);
        Assert.Same(rules, game.Breakout);
        Assert.Equal(600, game.CatchLineY);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public void BallCountMustBePositive(int count) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        new BreakoutSettings(count, 1.0 / 3, 12, [new(450, 200, 100, 30)]));

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1)] [InlineData(2)]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void CatchFractionMustBeFiniteStrictlyInsideUnitInterval(double fraction) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakoutSettings(3, fraction, 12, [new(450, 200, 100, 30)]));

    [Fact]
    public void BounceRuleAndLayoutCountAreExplicitAndBounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakoutSettings(3, 1.0 / 3, -1, [new(450, 200, 100, 30)]));
        Assert.Throws<ArgumentNullException>(() => Rules(null!));
        Assert.Throws<ArgumentException>(() => Rules([null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules(Enumerable.Repeat(new BrickSettings(450, 200, 100, 30), BreakoutSettings.MaximumBrickCount + 1).ToArray()));
        Assert.Equal(BreakoutSettings.MaximumBrickCount,
            Rules(Enumerable.Repeat(new BrickSettings(450, 200, 100, 30), BreakoutSettings.MaximumBrickCount).ToArray()).Bricks.Count);
        Assert.Equal(0, new BreakoutSettings(1, .25, 0, [new(450, 200, 100, 30)]).MaxBouncesWithoutBrick);
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void BrickCoordinatesAndSizesMustBeFinite(double invalid)
    {
        Assert.ThrowsAny<ArgumentException>(() => new BrickSettings(invalid, 200, 100, 30));
        Assert.ThrowsAny<ArgumentException>(() => new BrickSettings(450, invalid, 100, 30));
        Assert.ThrowsAny<ArgumentException>(() => new BrickSettings(450, 200, invalid, 30));
        Assert.ThrowsAny<ArgumentException>(() => new BrickSettings(450, 200, 100, invalid));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public void BrickSizeMustBePositive(double invalid)
    {
        Assert.ThrowsAny<ArgumentException>(() => new BrickSettings(450, 200, invalid, 30));
        Assert.ThrowsAny<ArgumentException>(() => new BrickSettings(450, 200, 100, invalid));
    }

    [Fact]
    public void FiniteEdgesMustNotOverflow() => Assert.Throws<ArgumentException>(() => new BrickSettings(double.MaxValue, 0, double.MaxValue, 30));

    public static IEnumerable<object[]> InvalidLayouts()
    {
        yield return [new BrickSettings(49, 200, 100, 30)];
        yield return [new BrickSettings(900, 200, 60, 30)];
        yield return [new BrickSettings(450, 49, 100, 30)];
        yield return [new BrickSettings(450, 590, 100, 20)];
        yield return [new BrickSettings(450, 200, 1e-10, 30)];
        yield return [new BrickSettings(450, 200, 100, 1e-10)];
        yield return [new BrickSettings(450, 200, 1e-30, 30)];
    }
    [Theory] [MemberData(nameof(InvalidLayouts))]
    public void InvalidLogicalAndNativeLayoutRejectsBeforeWorldCreation(BrickSettings brick) =>
        Assert.Throws<ArgumentException>(() => Validate(Rules([brick])));

    [Fact]
    public void OverlapRejectedButTouchingRectanglesAllowed()
    {
        Assert.Throws<ArgumentException>(() => Validate(Rules([new(450, 200, 100, 30), new(500, 210, 100, 30)])));
        Assert.Throws<ArgumentException>(() => Validate(Rules([new(450, 200, 100, 30), new(450, 200, 100, 30)])));
        var game = Validate(Rules([new(450, 200, 50, 30), new(500, 200, 50, 30)]));
        Assert.Equal(2, game.Breakout!.Bricks.Count);
    }

    [Fact]
    public void SpawnMustBeInCatchZoneAndClearOfWholeBrickCircle()
    {
        Assert.Throws<ArgumentException>(() => Validate(Rules([new(450, 200, 100, 30)]), new(10, 600, 500, 599, 0, -1)));
        Assert.Throws<ArgumentException>(() => Validate(Rules([new(450, 580, 100, 20)]), new(10, 600, 500, 600, 0, -1)));
        Assert.Throws<ArgumentException>(() => Validate(Rules([new(450, 580, 100, 20)]), new(10, 600, 500, 610, 0, -1)));
        var onLine = Validate(Rules([new(100, 120, 80, 25)]), new(10, 600, 500, 600, 0, -1));
        Assert.True(onLine.IsInCatchZone(onLine.Ball.Spawn.Y));
    }

    [Fact]
    public void FractionMustLeaveRepresentableRoomAboveAndBelowLine()
    {
        Assert.Throws<ArgumentException>(() => Validate(new(3, .99, 12, [new(450, 200, 100, 30)])));
        Assert.Throws<ArgumentException>(() => Validate(new(3, double.Epsilon, 12, [new(450, 200, 100, 30)])));
    }
}
