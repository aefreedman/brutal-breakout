using Brutal.Numerics;
using CrushKit.Brutal.Draw2D;

namespace BrutalBreakout.Presentation;

/// <summary>A fixed design canvas fitted to the current physical framebuffer, without text assets.</summary>
public static class ShapeShowcase
{
    // Deliberate gallery backdrop, independent of the playable game's swappable scheme.
    public static readonly LinearColor Background = new(.025f, .06f, .10f, 1);
    public const int ShapeCount = 64;

    public readonly record struct DecorativeEvidence(int Angled, int Chevron, int AngledIndices, int ChevronIndices);

    public static ShapeFrame Build(ShapeDrawList draw, int width, int height, out DecorativeEvidence decorative)
    {
        var scale = MathF.Min(width / 1600f, height / 960f);
        var x = (width - 1600 * scale) / 2;
        var y = (height - 960 * scale) / 2;
        draw.BeginFrame(width, height, new float3x2(scale, 0, 0, scale, x, y));
        var panel = LinearColor.FromSrgb(.12f, .17f, .23f);
        var cyan = LinearColor.FromSrgb(.24f, .84f, .85f);
        var gold = LinearColor.FromSrgb(1, .73f, .30f);
        var coral = LinearColor.FromSrgb(.98f, .40f, .42f);
        var white = LinearColor.FromSrgb(.88f, .94f, 1);
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 4; column++)
                draw.FillRoundedRect(new(24 + column * 392, 48 + row * 300), new(368, 276), 18, panel);
        // Asymmetric orientation markers: red at upper left, green at upper right.
        draw.FillRect(new(24, 20), new(44, 8), new(1, 0, 0));
        draw.FillRect(new(1524, 20), new(44, 8), new(0, 1, 0));

        // Filled primitives retained as the first panel's vocabulary.
        draw.FillDisc(new(104, 132), 42, cyan);
        draw.FillRing(new(264, 132), 25, 42, gold);
        draw.FillRect(new(60, 222), new(70, 54), coral);
        draw.FillRoundedRect(new(160, 222), new(80, 54), 16, cyan);
        draw.FillTriangle(new(278, 276), new(314, 218), new(350, 276), gold);

        // Solid hollow shapes, all centered on their input boundaries.
        draw.StrokeCircle(new(500, 132), 44, 9, cyan);
        draw.StrokeRect(new(598, 88), new(112, 88), 9, gold);
        draw.StrokeRoundedRect(new(452, 218), new(112, 66), 20, 9, coral);
        draw.StrokeTriangle(new(618, 280), new(662, 210), new(718, 280), 9, white);

        // Thin white input boundaries: inside, centered, outside, left to right.
        for (var i = 0; i < 3; i++)
        {
            var center = new float2(870 + i * 120, 184);
            draw.StrokeCircle(center, 38, 16, i == 0 ? cyan : i == 1 ? gold : coral,
                i == 0 ? StrokeAlignment.Inside : i == 1 ? StrokeAlignment.Center : StrokeAlignment.Outside);
            draw.StrokeCircle(center, 38, 1, white);
        }

        // Same 6-unit widths under two nonuniform scales: Local above, framebuffer below.
        for (var i = 0; i < 2; i++)
        {
            draw.SetTransform(new float3x2(1.2f + i * .6f, 0, 0, .8f, 70 + i * 164, 390));
            draw.StrokeRect(new(0, 0), new(64, 70), 6, cyan, space: StrokeSpace.Local);
            draw.StrokeRect(new(0, 150), new(64, 70), 6, gold, space: StrokeSpace.Framebuffer);
        }
        draw.SetTransform(float3x2.Identity);

        // Equal occupied intervals: basic/butt, square, round. Gaps allow cap extensions.
        draw.StrokePatternedSegment(new(456, 398), new(744, 398), 12,
            StrokePattern.Dashes(32, 24), cyan);
        draw.StrokePatternedSegment(new(456, 454), new(744, 454), 12,
            StrokePattern.RelativeDashes(2.6666667, 2), gold, LineCap.Square);
        draw.StrokePatternedSegment(new(456, 510), new(744, 510), 12,
            StrokePattern.Dashes(32, 24), coral, LineCap.Round);
        draw.StrokePatternedSegment(new(456, 566), new(744, 566), 12,
            StrokePattern.RelativeDots(2.5), white);

        // Continuous arc length through a corner; no phase restart at the vertex.
        draw.StrokePatternedPolyline([new(852, 400), new(920, 400), new(920, 480)], 8,
            StrokePattern.Dashes(34, 22, offsetCycles: -.2), white);
        // Closed rings: phased fixed periods, natural spacing fitted to closure, true fixed dots.
        draw.StrokePatternedCircle(new(1064, 422), 46, 8,
            StrokePattern.FixedDashes(8, .5, .25), cyan, LineCap.Round);
        draw.StrokePatternedCircle(new(902, 556), 40, 8,
            StrokePattern.Dashes(22, 17, -.2, fitClosed: true), gold);
        draw.StrokePatternedCircle(new(1064, 556), 42, 9,
            StrokePattern.FixedDots(12, .25), coral);

        // Filled transform references and an open solid bevel path.
        draw.SetTransform(new float3x2(.866f, .5f, -.5f, .866f, 115, 725));
        draw.FillRoundedRect(new(-42, -24), new(84, 48), 12, cyan);
        draw.SetTransform(new float3x2(1.5f, .15f, .65f, .7f, 275, 725));
        draw.FillRing(new(0, 0), 22, 36, gold);
        draw.SetTransform(new float3x2(-1, 0, .3f, 1, 140, 845));
        draw.FillTriangle(new(-40, -28), new(40, -28), new(-40, 32), coral);
        draw.SetTransform(float3x2.Identity);
        draw.StrokePolyline([new(216, 856), new(251, 807), new(293, 852), new(340, 799)], 13, white);

        // Scissors are framebuffer pixels, independent of the current local/world transforms.
        PixelRect Clip(float left, float top, float right, float bottom) => new(
            (int)MathF.Round(x + left * scale), (int)MathF.Round(y + top * scale),
            (int)MathF.Round(x + right * scale), (int)MathF.Round(y + bottom * scale));
        draw.FillRect(new(452, 682), new(296, 204), new(.025f, .04f, .055f));
        draw.PushClip(Clip(468, 698, 732, 870));
        draw.FillDisc(new(468, 780), 92, cyan);
        draw.PushClip(Clip(550, 720, 700, 848));
        draw.FillRect(new(500, 680), new(260, 220), coral);
        draw.FillRing(new(625, 780), 42, 80, gold);
        draw.PopClip();
        draw.StrokeSegment(new(450, 858), new(760, 858), 12, LineCap.Round, white);
        draw.PopClip();

        // Linear half-alpha red/blue, with opposite submission order in the two rows.
        var red = new LinearColor(1, 0, 0, .5f);
        var blue = new LinearColor(0, 0, 1, .5f);
        draw.FillRect(new(852, 682), new(304, 204), new(0, 0, 0));
        draw.FillRect(new(876, 702), new(140, 64), red);
        draw.FillRect(new(956, 702), new(140, 64), blue);
        draw.FillRect(new(956, 800), new(140, 64), blue);
        draw.FillRect(new(876, 800), new(140, 64), red);
        // Opaque linear mid-gray reference for the sRGB attachment encode.
        draw.FillRect(new(1120, 702), new(16, 162), new(.5f, .5f, .5f));
        // Dedicated colors tag indexed decorative geometry for hidden smoke evidence, not just call metadata.
        var angledColor = LinearColor.FromSrgb(.40f, 1, .65f);
        var chevronColor = LinearColor.FromSrgb(.78f, .60f, 1);
        var angled = 0;
        var chevron = 0;
        var pattern = StrokePattern.Dashes(36, 28, offsetCycles: .15);
        // Fourth column: slanted stripes above, concave V ends below; +1, -1, half-depth.
        foreach (var modifier in new[] { 1d, -1d, .5d })
        {
            draw.StrokePatternedSegment(new(1240, 112 + angled * 80), new(1528, 112 + angled * 80), 18,
                pattern.WithShape(DashShape.Angled, modifier), angledColor);
            angled++;
            draw.StrokePatternedSegment(new(1240, 412 + chevron * 80), new(1528, 412 + chevron * 80), 18,
                pattern.WithShape(DashShape.Chevron, modifier), chevronColor);
            chevron++;
        }
        // Closed, corner-crossing paths exercise the continuous decorative miter ribbon and seam.
        draw.StrokePatternedPolyline([new(1248, 716), new(1352, 716), new(1352, 848), new(1248, 848)], 6,
            StrokePattern.FixedDashes(8, .6, .13).WithShape(DashShape.Angled, -1), angledColor, closed: true);
        angled++;
        draw.StrokePatternedPolyline([new(1400, 716), new(1504, 716), new(1504, 848), new(1400, 848)], 6,
            StrokePattern.FixedDashes(8, .6, .13).WithShape(DashShape.Chevron, 1), chevronColor, closed: true);
        chevron++;
        var frame = draw.Finish();
        var angledIndices = 0;
        var chevronIndices = 0;
        foreach (var index in frame.Indices.Span)
        {
            var color = frame.Vertices.Span[(int)index].Color;
            if (color.Equals(new float4(angledColor.R, angledColor.G, angledColor.B, 1))) angledIndices++;
            if (color.Equals(new float4(chevronColor.R, chevronColor.G, chevronColor.B, 1))) chevronIndices++;
        }
        decorative = new(angled, chevron, angledIndices, chevronIndices);
        return frame;
    }
}
