namespace SimLab.App.Maps.Mountain;

/// <summary>
/// The far scenery beyond the mountain's 4 km grid, out to 15 km: ridged peaks 1 100–1 900 m above the strip
/// (2 600–3 400 m) to the east, north and south, and the valley running on to the west at −300…+200 m, so the sun
/// sets over it. The ground mix blends from the grid edge's over the first <see cref="EdgeBlend"/> metres, so the ring
/// meets the terrain without a colour seam; the height rises from the edge's over <see cref="RiseDistance"/>, so the
/// peaks stand back as distant ranges rather than walling in the grid (a 1 500 m blend raised a 1.5–2 km cliff right
/// at the grid edge, 2 km from the pilot).
/// </summary>
public sealed class MountainBackdrop(HeightGrid grid, MountainSurface surface)
{
    const double EdgeBlend = 1500, RiseDistance = 6000;
    const double PeakBase = 1100, PeakRise = 800, PeakScale = 2500;
    const double ValleyMid = -50, ValleyAmplitude = 250, ValleyScale = 3000;

    /// <summary>The valley fills the directions within about 25° of due west, fading out by about 55°.</summary>
    const double ValleyCosFull = 0.9, ValleyCosNone = 0.6;

    /// <summary>Finite-difference step for the slope that turns steep ground to rock (m), and the slopes over which the
    /// rock comes in: lower than on the grid, since the coarse ring (100 m to 1 km between vertices) shows the
    /// peaks' broad faces, not their crags.</summary>
    const double SlopeStep = 100, RockSlopeLow = 0.35, RockSlopeHigh = 0.65;

    /// <summary>The ring has no trees, so its forest is drawn as grass darkened by part needles — the canopy's dark
    /// green seen from afar, not the brown floor under it.</summary>
    const double ForestCover = 0.35;

    /// <summary>Above the grass the ground is mostly rock and scree, with some turf left between.</summary>
    const double RockAbove = 0.8;

    static readonly Noise2 Noise = new(MountainMap.Seed + 202);

    /// <summary>Height at (x, y) outside the grid: the grid's edge height there, blended into the far relief.</summary>
    public double Height(double x, double y)
    {
        double edge = grid.Height(x, y); // clamped to the grid's edge
        return edge + (Far(x, y) - edge) * MountainSurface.SmoothStep(Beyond(x, y) / RiseDistance);
    }

    /// <summary>
    /// Ground mix at (x, y) outside the grid: forest (dark grass) below +500 m, alpine grass above, giving way to rock from +750 to
    /// +1 050 m (2 250–2 550 m) and to snow from +1 250 to +1 400 m (2 750–2 900 m, the summer snow line, so only the
    /// high tops are white), with rock on steep ground whatever the height — blended from the map's own mix at the
    /// nearest grid edge point.
    /// </summary>
    public SurfaceWeights Surface(double x, double y)
    {
        double half = grid.HalfSize;
        var edge = surface.At(Math.Clamp(x, -half, half), Math.Clamp(y, -half, half));
        double t = MountainSurface.SmoothStep(Beyond(x, y) / EdgeBlend);
        if (t <= 0) return edge;

        double z = Height(x, y);
        double dx = (Height(x + SlopeStep, y) - Height(x - SlopeStep, y)) / (2 * SlopeStep);
        double dy = (Height(x, y + SlopeStep) - Height(x, y - SlopeStep)) / (2 * SlopeStep);
        double slope = Math.Sqrt(dx * dx + dy * dy);
        var far = SurfaceWeights.Only(SurfaceKind.Grass)
            .Toward(SurfaceKind.Needles, ForestCover * (1 - MountainSurface.SmoothStep((z - 450) / 100)))
            .Toward(SurfaceKind.Rock, RockAbove * MountainSurface.SmoothStep((z - 750) / 300))
            .Toward(SurfaceKind.Snow, MountainSurface.SmoothStep((z - 1250) / 150))
            .Toward(SurfaceKind.Rock, MountainSurface.SmoothStep((slope - RockSlopeLow) / (RockSlopeHigh - RockSlopeLow)));
        return Lerp(edge, far, t);
    }

    /// <summary>The peaks, giving way to the valley toward the west.</summary>
    static double Far(double x, double y)
    {
        double peaks = PeakBase + PeakRise * Noise.Ridged(x / PeakScale, y / PeakScale, 5);
        double valley = ValleyMid + ValleyAmplitude * Noise.Fbm(x / ValleyScale, y / ValleyScale, 4);
        double r = Math.Sqrt(x * x + y * y), west = r < 1e-9 ? 0 : -x / r;
        return peaks + (valley - peaks) * MountainSurface.SmoothStep((west - ValleyCosNone) / (ValleyCosFull - ValleyCosNone));
    }

    /// <summary>Horizontal distance outside the grid's square.</summary>
    double Beyond(double x, double y)
    {
        double dx = Math.Max(Math.Abs(x) - grid.HalfSize, 0), dy = Math.Max(Math.Abs(y) - grid.HalfSize, 0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    static SurfaceWeights Lerp(SurfaceWeights a, SurfaceWeights b, double t) => new(
        a.Grass + (b.Grass - a.Grass) * t, a.MowedGrass + (b.MowedGrass - a.MowedGrass) * t,
        a.Dirt + (b.Dirt - a.Dirt) * t, a.Gravel + (b.Gravel - a.Gravel) * t,
        a.Wheat + (b.Wheat - a.Wheat) * t, a.Ploughed + (b.Ploughed - a.Ploughed) * t,
        a.Rock + (b.Rock - a.Rock) * t, a.Snow + (b.Snow - a.Snow) * t, a.Needles + (b.Needles - a.Needles) * t);
}
