using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Club;

/// <summary>
/// The generic club field (world ENU): a grass runway east–west centred on the origin, the pilot box south of it and
/// the pits behind; farmland, groves, a road with a power line beyond. The relief is flat within 300 m and rolls gently
/// further out.
/// </summary>
public static class ClubMap
{
    public const string Id = "club";
    public const int Seed = 7;
    public const double HalfSize = 1000;
    public const double FlatRadius = 300;
    public const double BlendWidth = 300;
    public const double HillAmplitude = 8;
    public const double FarmlandRadius = 350;
    public const double RoadY = -220;
    public const double TrackX = -50;
    public const double PowerLineY = -229;

    public static readonly MapLayout Layout = new(
        PilotPosition: new Vec3(0, -25, 0), EyeHeight: 1.7, WindsockPosition: new Vec3(20, -28, 0),
        RunwayCentre: Vec3.Zero, RunwayLength: 100, RunwayWidth: 15, RunwayHeadingDeg: 90);

    public static readonly MapAmbience Ambience = new(
        SkyTop: new Rgb(0.28f, 0.48f, 0.80f), SkyHorizon: new Rgb(0.66f, 0.76f, 0.88f),
        GroundHorizon: new Rgb(0.35f, 0.38f, 0.30f), GroundBottom: new Rgb(0.12f, 0.15f, 0.10f), FogDensity: 0.00008);

    /// <summary>The dirt track from the pits to the road, and the gravel road.</summary>
    public static readonly IReadOnlyList<MapOverlay> Overlays =
    [
        new(SurfaceKind.Dirt, [(TrackX, -48), (TrackX, RoadY)], 3.5),
        new(SurfaceKind.Gravel, [(-HalfSize, RoadY), (HalfSize, RoadY)], 5),
    ];

    public static FieldMap Create()
    {
        var props = new List<Prop>();
        props.AddRange(ClubPlanting.Plant(Seed));
        props.AddRange(ClubFurniture.Place());
        return new FieldMap(Id, "FIELD_CLUB", HeightGrid.Sample(HalfSize, 5, Height), Surface, Layout, Ambience, props, Overlays);
    }

    /// <summary>Ground height at (x east, y north). The hill formula was authored with z = south, hence z = −y.</summary>
    public static double Height(double x, double y)
    {
        double z = -y;
        double r = Math.Sqrt(x * x + z * z);
        double blend = SmoothStep((r - FlatRadius) / BlendWidth);
        if (blend <= 0) return 0;
        double h = 0.55 * Math.Sin(x / 137.0 + 0.3) * Math.Cos(z / 191.0 - 1.1)
                 + 0.30 * Math.Sin((x + z) / 83.0 + 2.0)
                 + 0.15 * Math.Cos((x - 2 * z) / 59.0);
        return HillAmplitude * blend * (h + 1.0) * 0.5;
    }

    /// <summary>Tall grass, a mowed apron around the runway, gravel pits behind the pilot, farmland far out.</summary>
    public static SurfaceWeights Surface(double x, double y)
    {
        var w = SurfaceWeights.Only(SurfaceKind.Grass);
        double farm = SmoothStep((Math.Sqrt(x * x + y * y) - FarmlandRadius) / 40);
        if (farm > 0) w = w.Toward(ClubParcels.KindAt(x, y), farm);
        w = w.Toward(SurfaceKind.MowedGrass, RectBlend(x, y, -70, -22, 70, 22, 4));
        w = w.Toward(SurfaceKind.Gravel, RectBlend(x, y, -65, -48, 12, -27, 3));
        return w;
    }

    /// <summary>Kept clear of vegetation: the runway with its safety margins, the pilot box and the pits.</summary>
    public static bool KeepOut(double x, double y) => Math.Abs(x) < 110 && Math.Abs(y) < 60;

    /// <summary>Strips kept clear of trees: the road, the power line and the track.</summary>
    public static bool InCorridor(double x, double y) =>
        Math.Abs(y - RoadY) < 9 || Math.Abs(y - PowerLineY) < 12 || (Math.Abs(x - TrackX) < 6 && y < -40 && y > RoadY);

    /// <summary>1 inside the rectangle, 0 from <paramref name="edge"/> metres outside it, smooth in between.</summary>
    static double RectBlend(double x, double y, double minX, double minY, double maxX, double maxY, double edge)
    {
        double dx = Math.Max(Math.Max(minX - x, x - maxX), 0), dy = Math.Max(Math.Max(minY - y, y - maxY), 0);
        return 1 - SmoothStep(Math.Sqrt(dx * dx + dy * dy) / edge);
    }

    internal static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
