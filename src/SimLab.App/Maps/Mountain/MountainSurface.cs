namespace SimLab.App.Maps.Mountain;

/// <summary>
/// The mountain's ground mix, read off the finished height grid so it matches what is drawn and where the trees
/// stand: alpine grass by default, needles under the forest (but for the soaring beat, kept clear of trees, which stays
/// meadow), scree below the cliff bands, snow high up (lower on
/// north faces), rock on steep ground and on the bands, then the mowed strip and the gravel car park. The road and the
/// stream are ribbons laid on top (<see cref="MountainMap.Create"/>).
/// </summary>
public sealed class MountainSurface(HeightGrid grid)
{
    /// <summary>The forest floor is never quite all needles: some grass shows through.</summary>
    const double NeedleCover = 0.9, ScreeCover = 0.9;

    /// <summary>The needles give way to grass over the soaring beat's outer 40 m.</summary>
    const double BeatFade = 40;

    /// <summary>Rock from slope 0.65 (33°) to 0.95 (44°), tan of the ground angle.</summary>
    const double RockSlopeLow = 0.65, RockSlopeHigh = 0.95;

    /// <summary>The snow blends in from +820 to +900 m, 40 m lower where the ground faces north.</summary>
    const double SnowLow = 820, SnowHigh = 900, NorthSnowDrop = 40;

    const double StripMargin = 5, EdgeBlend = 2;
    const double CarParkMinX = 185, CarParkMaxX = 235, CarParkMinY = -85, CarParkMaxY = -55;

    public SurfaceWeights At(double x, double y)
    {
        var n = grid.Normal(x, y);
        double z = grid.Height(x, y), slope = Math.Sqrt(1 - n.Z * n.Z) / n.Z;
        var (bandRock, scree) = MountainRelief.CliffBands(x, y);

        var w = SurfaceWeights.Only(SurfaceKind.Grass);
        double forest = MountainPlanting.ForestDensity(x, y, z) * (1 - MountainMap.InSoaringBeat(x, y, BeatFade));
        w = w.Toward(SurfaceKind.Needles, NeedleCover * forest);
        w = w.Toward(SurfaceKind.Gravel, ScreeCover * scree);
        double snowLine = NorthSnowDrop * SmoothStep((n.Y - 0.25) / 0.1);
        w = w.Toward(SurfaceKind.Snow, SmoothStep((z - SnowLow + snowLine) / (SnowHigh - SnowLow)));
        w = w.Toward(SurfaceKind.Rock, Math.Max(SmoothStep((slope - RockSlopeLow) / (RockSlopeHigh - RockSlopeLow)), bandRock));

        var l = MountainMap.Layout;
        double halfLength = l.RunwayLength / 2 + StripMargin, halfWidth = l.RunwayWidth / 2 + StripMargin;
        w = w.Toward(SurfaceKind.MowedGrass, RectBlend(x, y, l.RunwayCentre.X - halfLength, l.RunwayCentre.Y - halfWidth,
            l.RunwayCentre.X + halfLength, l.RunwayCentre.Y + halfWidth));
        w = w.Toward(SurfaceKind.Gravel, RectBlend(x, y, CarParkMinX, CarParkMinY, CarParkMaxX, CarParkMaxY));
        return w;
    }

    /// <summary>1 inside the rectangle, 0 from <see cref="EdgeBlend"/> metres outside it, smooth in between.</summary>
    static double RectBlend(double x, double y, double minX, double minY, double maxX, double maxY)
    {
        double dx = Math.Max(Math.Max(minX - x, x - maxX), 0), dy = Math.Max(Math.Max(minY - y, y - maxY), 0);
        return 1 - SmoothStep(Math.Sqrt(dx * dx + dy * dy) / EdgeBlend);
    }

    internal static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
