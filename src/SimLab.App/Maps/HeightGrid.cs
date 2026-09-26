using SimLab.Flight.Geometry;

namespace SimLab.App.Maps;

/// <summary>
/// Ground heights on a square grid (world x east, y north → z up). Each cell is split into two triangles along its
/// (i, j)–(i+1, j+1) diagonal, exactly as the game's terrain mesh, so the height the aircraft touches is the height
/// drawn. Outside the grid the coordinates are clamped to its edge.
/// </summary>
public sealed class HeightGrid
{
    readonly float[] _h;

    public HeightGrid(double minX, double minY, double step, int count, float[] heights)
    {
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(count), count, "A grid needs at least 2 × 2 points.");
        if (heights.Length != count * count) throw new ArgumentException($"Expected {count * count} heights, got {heights.Length}.", nameof(heights));
        MinX = minX; MinY = minY; Step = step; Count = count; _h = heights;
    }

    /// <summary>The height function sampled every <paramref name="step"/> over [−halfSize, halfSize]².</summary>
    public static HeightGrid Sample(double halfSize, double step, Func<double, double, double> height)
    {
        int count = (int)Math.Round(2 * halfSize / step) + 1;
        var h = new float[count * count];
        Parallel.For(0, count, j =>
        {
            double y = -halfSize + j * step;
            for (int i = 0; i < count; i++) h[j * count + i] = (float)height(-halfSize + i * step, y);
        });
        return new HeightGrid(-halfSize, -halfSize, step, count, h);
    }

    public double MinX { get; }
    public double MinY { get; }
    public double Step { get; }
    public int Count { get; }
    public double MaxX => MinX + (Count - 1) * Step;
    public double MaxY => MinY + (Count - 1) * Step;

    /// <summary>Grids are square and centred on the origin here.</summary>
    public double HalfSize => (Count - 1) * Step / 2;

    /// <summary>i along x (east), j along y (north).</summary>
    public float this[int i, int j] => _h[j * Count + i];

    public double Height(double x, double y)
    {
        var (i, j, fx, fy) = Locate(x, y);
        double a = this[i, j], d = this[i + 1, j + 1];
        return fx >= fy
            ? a + fx * (this[i + 1, j] - a) + fy * (d - this[i + 1, j])
            : a + fy * (this[i, j + 1] - a) + fx * (d - this[i, j + 1]);
    }

    public Vec3 Normal(double x, double y)
    {
        var (i, j, fx, fy) = Locate(x, y);
        double a = this[i, j], d = this[i + 1, j + 1], dx, dy;
        if (fx >= fy) { double b = this[i + 1, j]; dx = (b - a) / Step; dy = (d - b) / Step; }
        else { double c = this[i, j + 1]; dy = (c - a) / Step; dx = (d - c) / Step; }
        return new Vec3(-dx, -dy, 1).Normalized();
    }

    (int I, int J, double Fx, double Fy) Locate(double x, double y)
    {
        double u = Math.Clamp((x - MinX) / Step, 0, Count - 1), v = Math.Clamp((y - MinY) / Step, 0, Count - 1);
        int i = Math.Min((int)u, Count - 2), j = Math.Min((int)v, Count - 2);
        return (i, j, u - i, v - j);
    }
}
