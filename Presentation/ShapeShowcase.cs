using Brutal.Numerics;
using BrutalBreakout.Rendering.Shapes;

namespace BrutalBreakout.Presentation;

/// <summary>A fixed design canvas fitted to the current physical framebuffer, without text assets.</summary>
internal static class ShapeShowcase
{
    public const int ShapeCount = 31;

    public static ShapeFrame Build(ShapeDrawList draw, int width, int height)
    {
        var scale = MathF.Min(width / 1200f, height / 660f);
        var x = (width - 1200 * scale) / 2;
        var y = (height - 660 * scale) / 2;
        draw.BeginFrame(width, height, new float3x2(scale, 0, 0, scale, x, y));
        var panel = LinearColor.FromSrgb(.12f, .17f, .23f);
        var cyan = LinearColor.FromSrgb(.24f, .84f, .85f);
        var gold = LinearColor.FromSrgb(1, .73f, .30f);
        var coral = LinearColor.FromSrgb(.98f, .40f, .42f);
        var white = LinearColor.FromSrgb(.88f, .94f, 1);
        for (var row = 0; row < 2; row++)
            for (var column = 0; column < 3; column++)
                draw.FillRoundedRect(new(24 + column * 392, 48 + row * 300), new(368, 276), 18, panel);
        // Asymmetric orientation markers: red at upper left, green at upper right.
        draw.FillRect(new(24, 20), new(44, 8), new(1, 0, 0));
        draw.FillRect(new(1132, 20), new(44, 8), new(0, 1, 0));

        draw.FillDisc(new(123, 184), 62, cyan);
        draw.FillRing(new(290, 184), 38, 62, gold);

        draw.FillRect(new(444, 105), new(120, 72), coral);
        draw.FillRoundedRect(new(602, 105), new(150, 72), 24, cyan);
        draw.FillTriangle(new(524, 274), new(600, 199), new(676, 274), gold);

        draw.StrokeSegment(new(884, 116), new(1100, 116), 28, LineCap.Butt, cyan);
        draw.StrokeSegment(new(884, 184), new(1100, 184), 28, LineCap.Square, gold);
        draw.StrokeSegment(new(884, 252), new(1100, 252), 28, LineCap.Round, coral);

        // Local rotation, nonuniform scale/shear, and reflection, all before canvas fitting.
        draw.SetTransform(new float3x2(.866f, .5f, -.5f, .866f, 115, 425));
        draw.FillRoundedRect(new(-42, -24), new(84, 48), 12, cyan);
        draw.SetTransform(new float3x2(1.5f, .15f, .65f, .7f, 275, 425));
        draw.FillRing(new(0, 0), 22, 36, gold);
        draw.SetTransform(new float3x2(-1, 0, .3f, 1, 140, 545));
        draw.FillTriangle(new(-40, -28), new(40, -28), new(-40, 32), coral);
        draw.SetTransform(float3x2.Identity);
        draw.StrokePolyline([new(216, 556), new(251, 507), new(293, 552), new(340, 499)], 13, white);

        // Scissors are framebuffer pixels, independent of the current local/world transforms.
        PixelRect Clip(float left, float top, float right, float bottom) => new(
            (int)MathF.Round(x + left * scale), (int)MathF.Round(y + top * scale),
            (int)MathF.Round(x + right * scale), (int)MathF.Round(y + bottom * scale));
        draw.FillRect(new(452, 382), new(296, 204), new(.025f, .04f, .055f));
        draw.PushClip(Clip(468, 398, 732, 570));
        draw.FillDisc(new(468, 480), 92, cyan);
        draw.PushClip(Clip(550, 420, 700, 548));
        draw.FillRect(new(500, 380), new(260, 220), coral);
        draw.FillRing(new(625, 480), 42, 80, gold);
        draw.PopClip();
        draw.StrokeSegment(new(450, 558), new(760, 558), 12, LineCap.Round, white);
        draw.PopClip();

        // Linear half-alpha red/blue, with opposite submission order in the two rows.
        var red = new LinearColor(1, 0, 0, .5f);
        var blue = new LinearColor(0, 0, 1, .5f);
        draw.FillRect(new(852, 382), new(304, 204), new(0, 0, 0));
        draw.FillRect(new(876, 402), new(140, 64), red);
        draw.FillRect(new(956, 402), new(140, 64), blue);
        draw.FillRect(new(956, 500), new(140, 64), blue);
        draw.FillRect(new(876, 500), new(140, 64), red);
        // Opaque linear mid-gray reference for the sRGB attachment encode.
        draw.FillRect(new(1120, 402), new(16, 162), new(.5f, .5f, .5f));
        return draw.Finish();
    }
}
