using Brutal.Numerics;
using BrutalBreakout.Rendering.Shapes;
using Xunit;

namespace Shapes.Tests;

public class AnimatedRingTests
{
    private static readonly LinearColor Half = new(1, 1, 1, .5f);
    private static double Cross(float2 a, float2 b, float2 c) =>
        ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);

    [Fact]
    public void DefaultHeldRingPreviouslyInvertedAnEarAtThisPhase()
    {
        using var draw = new ShapeDrawList();
        draw.BeginFrame(1280, 720);
        // Red on the original implementation: source ear area -0.000672 becomes +0.000293 in FLOAT32.
        draw.StrokePatternedCircle(new(640, 612), 144, 14.4f,
            StrokePattern.FixedDashes(16, .5, 0.11764605882352941), Half, LineCap.Round, StrokeSpace.Framebuffer);
        var frame = draw.Finish();
        Validate(frame, 144, 14.4);
        AssertNoOverlap(frame);
    }

    [Theory]
    [InlineData(1280, 720, 0f)]
    [InlineData(960, 540, .123f)]
    [InlineData(720, 1280, .456f)]
    [InlineData(2000, 1800, .789f)]
    public void AnimationBoundaryAndSeamPhasesSurviveResizeLetterboxAndSubpixelCenters(int w, int h, float fraction)
    {
        using var draw = new ShapeDrawList();
        float scale = MathF.Min(w / 1280f, h / 720f);
        float radius = (float)(Math.Min(w, h) * .2 / scale);
        float width = (float)(Math.Min(w, h) * .02);
        // Cover ordinary phases, and cuts immediately around every sampled-circle boundary.
        // The source tolerance formula defines the deterministic tessellation fixture, not a tunable default.
        double r = radius * (double)scale;
        int segments = Math.Max(8, (int)Math.Ceiling(Math.PI / (2 * Math.Asin(Math.Sqrt((.25 - .09 - r * .000001) / (2 * r))))));
        segments = (segments + 3) / 4 * 4;
        var boundaryPhases = Enumerable.Range(0, segments / 4).SelectMany(i =>
            new[] { 0d, -.5 }.SelectMany(endpoint =>
                new[] { -1e-6, -1e-8, 0d, 1e-8, 1e-6 }.Select(e => i * 16d / segments + endpoint + e)));
        var phases = Enumerable.Range(0, 65).Select(i => i / 64d).Concat(boundaryPhases);
        foreach (double phase in phases)
        {
            draw.BeginFrame(w, h);
            draw.SetTransform(new(scale, 0, 0, scale, (w - 1280 * scale) / 2, (h - 720 * scale) / 2));
            var error = Record.Exception(() => draw.StrokePatternedCircle(new(640 + fraction, 612 - fraction), radius, width,
                StrokePattern.FixedDashes(16, .5, phase), Half, LineCap.Round, StrokeSpace.Framebuffer));
            Assert.True(error is null, $"phase={phase:R}, viewport={w}x{h}, fraction={fraction:R}: {error}");
            Validate(draw.Finish(), r, width);
        }
    }

    [Theory]
    [InlineData(StrokeSpace.Local, false)]
    [InlineData(StrokeSpace.Local, true)]
    [InlineData(StrokeSpace.Framebuffer, false)]
    [InlineData(StrokeSpace.Framebuffer, true)]
    public void QuantizedEarPredicatesRespectMeasurementSpaceAndReflectedTransforms(StrokeSpace space, bool reflected)
    {
        using var draw = new ShapeDrawList();
        foreach (double phase in new[] { 0d, .11764605882352941, .11764705882352941, .11764805882352941, .99999999 })
        {
            draw.BeginFrame(1280, 720, new(reflected ? -1 : 1, 0, 0, 1, reflected ? 1280 : 0, 0));
            draw.StrokePatternedCircle(new(640.125f, 612.375f), 144, 14.4f,
                StrokePattern.FixedDashes(16, .5, phase), Half, LineCap.Round, space);
            var frame = draw.Finish();
            Validate(frame, 144, 14.4);
            AssertNoOverlap(frame);
        }
    }

    [Fact]
    public void GenuineCollapsedPatternStillRejectsAndRestoresPriorOutput()
    {
        using var draw = new ShapeDrawList();
        draw.BeginFrame(1280, 720);
        draw.FillTriangle(new(0, 0), new(10, 0), new(0, 10), Half);
        draw.SetTransform(new(1, 0, 0, 1, 1000, 1000));
        Assert.Throws<ArgumentException>(() => draw.StrokePatternedSegment(new(0, 0), new(20, 0), .000001f,
            StrokePattern.Dashes(2, 2), Half, LineCap.Round));
        var frame = draw.Finish();
        Assert.Equal(3, frame.Indices.Length);
        Assert.Equal(3, frame.Vertices.Length);
        Assert.Equal(3, Assert.Single(frame.Commands.ToArray()).IndexCount);
    }

    private static void Validate(ShapeFrame frame, double radius, double width)
    {
        var vertices = frame.Vertices.Span;
        var ids = frame.Indices.Span;
        Assert.NotEmpty(frame.Indices.ToArray());
        Assert.Equal(0, ids.Length % 3);
        double sign = 0, area = 0;
        foreach (var vertex in vertices)
        {
            Assert.True(float.IsFinite(vertex.Position.X) && float.IsFinite(vertex.Position.Y));
            Assert.Equal(new float4(.5f), vertex.Color);
        }
        for (int i = 0; i < ids.Length; i += 3)
        {
            for (int j = 0; j < 3; j++) Assert.InRange(ids[i + j], 0u, (uint)vertices.Length - 1);
            double cross = Cross(vertices[(int)ids[i]].Position, vertices[(int)ids[i + 1]].Position, vertices[(int)ids[i + 2]].Position);
            Assert.NotEqual(0, cross);
            if (sign == 0) sign = Math.Sign(cross);
            Assert.Equal(sign, Math.Sign(cross));
            area += Math.Abs(cross) / 2;
        }
        // Half-duty centerline stroke plus 16 pairs of round caps. Sampling reserves the existing curve error.
        double ideal = Math.Tau * radius * .5 * width + 16 * Math.PI * width * width / 4;
        Assert.InRange(area, ideal * .98, ideal * 1.02);
    }

    private static void AssertNoOverlap(ShapeFrame frame)
    {
        var vertices = frame.Vertices.ToArray(); var ids = frame.Indices.ToArray();
        for (int i = 0; i < ids.Length; i += 3) for (int j = i + 3; j < ids.Length; j += 3)
        {
            bool separated = false;
            for (int side = 0; side < 2 && !separated; side++)
            {
                int first = side == 0 ? i : j;
                for (int edge = 0; edge < 3; edge++)
                {
                    var a = vertices[ids[first + edge]].Position;
                    var b = vertices[ids[first + (edge + 1) % 3]].Position;
                    double amin = double.PositiveInfinity, amax = double.NegativeInfinity, bmin = amin, bmax = amax;
                    for (int k = 0; k < 3; k++)
                    {
                        double x = Cross(a, b, vertices[ids[i + k]].Position), y = Cross(a, b, vertices[ids[j + k]].Position);
                        amin = Math.Min(amin, x); amax = Math.Max(amax, x); bmin = Math.Min(bmin, y); bmax = Math.Max(bmax, y);
                    }
                    if (amax <= bmin || bmax <= amin) { separated = true; break; }
                }
            }
            Assert.True(separated, $"Positive-area overlap between triangles {i / 3}/{j / 3}.");
        }
    }
}
