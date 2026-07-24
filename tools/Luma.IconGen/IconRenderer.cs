namespace Luma.IconGen;

/// <summary>
/// Renders the Luma mark (aperture ring + luma-gradient play triangle) into raw
/// RGBA pixels. Geometry mirrors <c>src/Luma.Presentation/Assets/luma-icon.svg</c>,
/// which stays the design source of truth. Edges are antialiased by supersampling.
/// </summary>
public static class IconRenderer
{
    private const int Supersample = 4;

    // Design is authored on a 256x256 canvas; all constants below are in that space.
    private const double Canvas = 256;
    private const double CornerRadius = 56;
    private const double RingCenter = 128;
    private const double RingRadius = 74;
    private const double RingStroke = 14;

    private static readonly (double X, double Y)[] Triangle =
    [
        (110, 92), (176, 128), (110, 164)
    ];

    private static readonly double TriMinX = Triangle.Min(p => p.X);
    private static readonly double TriMaxX = Triangle.Max(p => p.X);
    private static readonly double TriMinY = Triangle.Min(p => p.Y);
    private static readonly double TriMaxY = Triangle.Max(p => p.Y);

    private static readonly (byte R, byte G, byte B) Background = (0x0E, 0x0F, 0x13);
    private static readonly (byte R, byte G, byte B) Ring = (0x2A, 0x2D, 0x37);
    private static readonly (byte R, byte G, byte B) GradientFrom = (0xFF, 0x7A, 0x45);
    private static readonly (byte R, byte G, byte B) GradientTo = (0x3F, 0xA9, 0xF5);

    /// <summary>Render the icon at <paramref name="size"/> px as RGBA (4 bytes per pixel).</summary>
    public static byte[] Render(int size)
    {
        var pixels = new byte[size * size * 4];
        var scale = Canvas / size;
        var step = 1.0 / Supersample;

        for (var py = 0; py < size; py++)
        {
            for (var px = 0; px < size; px++)
            {
                double r = 0, g = 0, b = 0, a = 0;

                // Supersample this pixel on a Supersample x Supersample grid.
                for (var sy = 0; sy < Supersample; sy++)
                {
                    for (var sx = 0; sx < Supersample; sx++)
                    {
                        var x = (px + (sx + 0.5) * step) * scale;
                        var y = (py + (sy + 0.5) * step) * scale;
                        var (sr, sg, sb, sa) = SampleAt(x, y);
                        r += sr; g += sg; b += sb; a += sa;
                    }
                }

                var n = Supersample * Supersample;
                var i = (py * size + px) * 4;
                pixels[i + 0] = (byte)Math.Round(r / n);
                pixels[i + 1] = (byte)Math.Round(g / n);
                pixels[i + 2] = (byte)Math.Round(b / n);
                pixels[i + 3] = (byte)Math.Round(a / n);
            }
        }

        return pixels;
    }

    /// <summary>Colour of the design at one point, in design space. Painter's order.</summary>
    private static (double R, double G, double B, double A) SampleAt(double x, double y)
    {
        // Outside the rounded tile everything is transparent.
        if (!InRoundedRect(x, y))
            return (0, 0, 0, 0);

        // The play triangle wins over the ring, which wins over the tile background.
        if (InTriangle(x, y))
        {
            // Gradient spans the triangle's own bounding box (as objectBoundingBox in
            // the SVG does), so the full orange->blue range is visible on the mark.
            var tx = (x - TriMinX) / (TriMaxX - TriMinX);
            var ty = (y - TriMinY) / (TriMaxY - TriMinY);
            var t = Math.Clamp((tx + ty) / 2.0, 0, 1);
            return (
                Lerp(GradientFrom.R, GradientTo.R, t),
                Lerp(GradientFrom.G, GradientTo.G, t),
                Lerp(GradientFrom.B, GradientTo.B, t),
                255);
        }

        var d = Math.Sqrt((x - RingCenter) * (x - RingCenter) + (y - RingCenter) * (y - RingCenter));
        if (Math.Abs(d - RingRadius) <= RingStroke / 2.0)
            return (Ring.R, Ring.G, Ring.B, 255);

        return (Background.R, Background.G, Background.B, 255);
    }

    private static bool InRoundedRect(double x, double y)
    {
        const double max = Canvas;
        if (x < 0 || y < 0 || x > max || y > max) return false;

        // Only the four corner quadrants need the radius test.
        var cx = x < CornerRadius ? CornerRadius : x > max - CornerRadius ? max - CornerRadius : x;
        var cy = y < CornerRadius ? CornerRadius : y > max - CornerRadius ? max - CornerRadius : y;
        if (cx == x || cy == y) return true;

        var dx = x - cx;
        var dy = y - cy;
        return dx * dx + dy * dy <= CornerRadius * CornerRadius;
    }

    private static bool InTriangle(double x, double y)
    {
        var (ax, ay) = Triangle[0];
        var (bx, by) = Triangle[1];
        var (cx, cy) = Triangle[2];

        var d1 = Edge(x, y, ax, ay, bx, by);
        var d2 = Edge(x, y, bx, by, cx, cy);
        var d3 = Edge(x, y, cx, cy, ax, ay);

        var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static double Edge(double px, double py, double ax, double ay, double bx, double by) =>
        (px - bx) * (ay - by) - (ax - bx) * (py - by);

    private static double Lerp(byte from, byte to, double t) => from + (to - from) * t;
}
