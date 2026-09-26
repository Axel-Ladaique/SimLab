using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Mountain;

/// <summary>
/// Deterministic mountain vegetation and rocks: fir stands with clearings between the valley floor and +600 m,
/// thinning with height, a few solitary firs on the shoulder meadows, boulders at the foot of the cliff bands and a
/// few on the meadows. Nothing in <see cref="MountainMap.KeepOut"/>, on steep ground or around the chalets.
/// </summary>
public static class MountainPlanting
{
    const double ForestLow = -350, ForestHigh = 600, MaxTreeSlope = 0.78;
    const double TreeSpacing = 9, MeadowEnd = 450;
    const int SolitaryFirs = 40, MeadowBoulders = 30, MaxCliffBoulders = 300;

    static readonly Rgb Fir = new(0.09f, 0.22f, 0.11f);
    static readonly Rgb Rock = new(0.42f, 0.41f, 0.39f);
    static readonly Noise2 Stands = new(MountainMap.Seed + 101);

    /// <summary>How much of the ground at (x, y) the forest covers, 0…1: stands where a broad noise exceeds a
    /// threshold that rises with height, so the forest thins toward +600 m and stops there. The shoulder between the
    /// crest and <see cref="MeadowEnd"/> stays open meadow.</summary>
    public static double ForestDensity(double x, double y) => ForestDensity(x, y, MountainRelief.Height(x, y));

    static double ForestDensity(double x, double y, double z)
    {
        if (z < ForestLow || z > ForestHigh) return 0;
        double meadow = x < MountainRelief.CrestX(y) ? 1 : SmoothStep((x - MeadowEnd) / 200);
        if (meadow <= 0) return 0;
        double threshold = 0.235 + 0.30 * (z - ForestLow) / (ForestHigh - ForestLow);
        return meadow * SmoothStep((Stands.Fbm(x / 350, y / 350, 4) - threshold) / 0.12);
    }

    public static IEnumerable<Prop> Plant(int seed, HeightGrid grid, MountainRoad road)
    {
        var rng = new Random(seed);
        var props = new List<Prop>();
        double edge = MountainMap.HalfSize - 10;

        double Between(double lo, double hi) => lo + rng.NextDouble() * (hi - lo);
        Rgb Tint(Rgb c, double variation)
        {
            float k = (float)Between(1 - variation, 1 + variation);
            return new Rgb(c.R * k, c.G * k, c.B * k);
        }
        double Slope(double x, double y)
        {
            var n = grid.Normal(x, y);
            return Math.Sqrt(1 - n.Z * n.Z) / n.Z;
        }
        bool Free(double x, double y, double top) => Math.Abs(x) < edge && Math.Abs(y) < edge
            && !MountainMap.KeepOut(x, y, tall: top > 2) && !InHamlet(x, y);
        Vec3 Ground(double x, double y) => new(x, y, grid.Height(x, y));

        // Random values are drawn before the checks, so rejecting one tree never shifts the others.
        void Conifer(double x, double y, Func<double, bool> accept)
        {
            double h = Between(12, 28), radius = Between(0.22, 0.30) * h, yaw = Between(0, 360), roll = rng.NextDouble();
            var tint = Tint(Fir, 0.10);
            double z = grid.Height(x, y);
            if (z < ForestLow || z > ForestHigh || !accept(roll)) return;
            if (Slope(x, y) > MaxTreeSlope || !Free(x, y, h)) return;
            props.Add(new ConiferTree(Ground(x, y), yaw, h, radius, tint));
        }

        // The forest: one candidate per cell of a jittered grid, kept with the stand density there.
        for (double y = -edge; y < edge; y += TreeSpacing)
        for (double x = -edge; x < edge; x += TreeSpacing)
        {
            double px = x + Between(0, TreeSpacing), py = y + Between(0, TreeSpacing);
            Conifer(px, py, roll => roll < ForestDensity(px, py, grid.Height(px, py)));
        }

        // Solitary firs on the shoulder meadows, outside the stands.
        for (int i = 0; i < SolitaryFirs; i++)
        {
            double y = Between(-1500, 1500), x = MountainRelief.CrestX(y) + Between(20, 700);
            Conifer(x, y, _ => ForestDensity(x, y, grid.Height(x, y)) < 0.05);
        }

        // Boulders at the foot of the cliff bands, then a few on the meadows.
        Boulder? MakeBoulder(double x, double y)
        {
            double u = rng.NextDouble(), radius = 0.8 + 3.2 * u * u, height = radius * Between(1.0, 1.5), yaw = Between(0, 360);
            var tint = Tint(Rock, 0.12);
            if (!Free(x, y, 0.75 * height) || road.Nearest(x, y, 8 + radius) is not null) return null;
            return new Boulder(Ground(x, y), yaw, radius, height, tint);
        }
        var feet = CliffFeet(grid, Slope);
        Shuffle(feet, rng);
        int placed = 0;
        foreach (var (x, y) in feet)
        {
            if (placed == MaxCliffBoulders) break;
            if (MakeBoulder(x + Between(-2, 2), y + Between(-2, 2)) is { } b) { props.Add(b); placed++; }
        }
        for (int attempts = 0, meadow = 0; meadow < MeadowBoulders && attempts < 5000; attempts++)
        {
            double y = Between(-1800, 1800), x = MountainRelief.CrestX(y) + Between(20, 1400);
            if (Slope(x, y) > 0.5 || ForestDensity(x, y, grid.Height(x, y)) > 0.05) continue;
            if (MakeBoulder(x, y) is { } b) { props.Add(b); meadow++; }
        }
        return props;
    }

    /// <summary>Points on the west face where the slope has just dropped below 0.6 after exceeding 1.0 a few metres
    /// upslope: the foot of a cliff band, where fallen rocks come to rest.</summary>
    static List<(double X, double Y)> CliffFeet(HeightGrid grid, Func<double, double, double> slope)
    {
        const double step = 6;
        var feet = new List<(double X, double Y)>();
        for (double y = -MountainMap.HalfSize + 10; y < MountainMap.HalfSize - 10; y += step)
        for (double x = -MountainMap.HalfSize + 10; x < MountainRelief.CrestX(y); x += step)
        {
            if (slope(x, y) >= 0.6) continue;
            var n = grid.Normal(x, y);
            double gx = -n.X, gy = -n.Y, g = Math.Sqrt(gx * gx + gy * gy);
            if (g < 1e-6) continue;
            gx /= g; gy /= g;
            for (double s = 4; s <= 16; s += 4)
                if (slope(x + gx * s, y + gy * s) > 1.0) { feet.Add((x, y)); break; }
        }
        return feet;
    }

    /// <summary>The clearing around the chalets and the car park.</summary>
    static bool InHamlet(double x, double y) => x > 180 && x < 330 && y > -140 && y < -10;

    static void Shuffle<T>(List<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
