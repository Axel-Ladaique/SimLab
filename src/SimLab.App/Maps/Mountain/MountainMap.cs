using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Mountain;

/// <summary>
/// The mountain slope site (world ENU, z = 0 at 1 500 m): a strip on the shoulder just east of a curved crest, the
/// pilot 6 m behind the crest facing a 350 m drop into a valley with a lake and a stream, a switchback road climbing
/// the face, and the ground rising east to a snowy summit. With wind from the west aircraft take off toward the drop.
/// </summary>
public static class MountainMap
{
    public const string Id = "mountain";
    public const int Seed = 11;
    public const double HalfSize = 2000, GridStep = 4, DatumElevationM = 1500, LakeLevel = -350;

    public static readonly MapLayout Layout = new(
        PilotPosition: new Vec3(-24, -22, 0), EyeHeight: 1.7, WindsockPosition: new Vec3(-18, -28, 0),
        RunwayCentre: new Vec3(95, 0, 0), RunwayLength: 130, RunwayWidth: 20, RunwayHeadingDeg: 90);

    /// <summary>A deeper blue sky and less haze than the lowland club (the ranges 5–15 km out hazed, not washed out).
    /// The rock and snow tints are warmer than the defaults, offsetting the cool light from that deep blue sky on the
    /// faces the sun does not reach, so rock reads grey and shaded snow white rather than lilac.</summary>
    public static readonly MapAmbience Ambience = new(
        SkyTop: new Rgb(0.22f, 0.42f, 0.78f), SkyHorizon: new Rgb(0.62f, 0.74f, 0.88f),
        GroundHorizon: new Rgb(0.30f, 0.34f, 0.30f), GroundBottom: new Rgb(0.10f, 0.12f, 0.10f), FogDensity: 0.00003)
    {
        TerrainTints = MapAmbience.DefaultTerrainTints.Select((tint, k) => (SurfaceKind)k switch
        {
            SurfaceKind.Rock => new Rgb(1.35f, 1.25f, 1.07f),
            SurfaceKind.Snow => new Rgb(1.1f, 1.04f, 0.91f),
            _ => tint,
        }).ToArray(),
    };

    /// <summary>The chairlift's line, from the lake's east shore up to the knob north of the strip.</summary>
    public static readonly (double X, double Y) LiftBottom = (-930, 560), LiftTop = (-150, 950);

    /// <summary>The switchback road, shared by the relief, the props and the keep-out.</summary>
    public static MountainRoad Road { get; } = new();

    const double StripMargin = 25, PilotClearing = 60, BeatDepth = 250, BeatHalfWidth = 350;
    const double RoadClearance = 8, LakeClearance = 5, StreamClearance = 4, LiftClearance = 12;

    public static FieldMap Create()
    {
        var grid = MountainRelief.Build(Road);
        var props = new List<Prop>();
        props.AddRange(MountainPlanting.Plant(Seed, grid, Road));
        props.AddRange(MountainFurniture.Place(grid, Road));
        var surface = new MountainSurface(grid);
        var backdrop = new MountainBackdrop(grid, surface);
        MapOverlay[] overlays =
        [
            new(SurfaceKind.Dirt, Road.Path, Road.Width),
            new(SurfaceKind.Gravel, MountainRelief.Stream, 3, Water: true),
        ];
        return new FieldMap(Id, "FIELD_MOUNTAIN", grid, surface.At, Layout, Ambience, props, overlays, [MountainRelief.Lake])
        {
            DatumElevationM = DatumElevationM,
            Backdrop = backdrop.Height,
            BackdropSurface = backdrop.Surface,
        };
    }

    /// <summary>
    /// Kept clear of props (but for the furniture placed there on purpose): the strip with 25 m margins, the pilot's
    /// 60 m clearing, the road, the lake shore, the stream banks, the chairlift corridor and, for anything
    /// <paramref name="tall"/> (over 2 m), the soaring beat on the face in front of the pilot.
    /// </summary>
    public static bool KeepOut(double x, double y, bool tall = true)
    {
        var (cx, cy) = (Layout.RunwayCentre.X, Layout.RunwayCentre.Y);
        if (Math.Abs(x - cx) < Layout.RunwayLength / 2 + StripMargin && Math.Abs(y - cy) < Layout.RunwayWidth / 2 + StripMargin) return true;
        var pilot = Layout.PilotPosition;
        if ((x - pilot.X) * (x - pilot.X) + (y - pilot.Y) * (y - pilot.Y) < PilotClearing * PilotClearing) return true;
        double crest = MountainRelief.CrestX(y);
        if (tall && x <= crest && x >= crest - BeatDepth && Math.Abs(y - pilot.Y) < BeatHalfWidth) return true;
        if (MountainRelief.OutsideLake(x, y) < MountainRelief.ShoreMargin + LakeClearance) return true;
        if (MountainRelief.StreamDistance(x, y) < StreamClearance) return true;
        if (LiftDistance(x, y) < LiftClearance) return true;
        return Road.Nearest(x, y, RoadClearance) is not null;
    }

    /// <summary>How far (x, y) lies within the soaring beat that <see cref="KeepOut"/> clears of tall props: 0 outside,
    /// rising to 1 over its outer <paramref name="fade"/> metres (from its west and side edges; the crest bounds it to
    /// the east, where the open shoulder begins).</summary>
    public static double InSoaringBeat(double x, double y, double fade)
    {
        double crest = MountainRelief.CrestX(y);
        if (x > crest) return 0;
        double inside = Math.Min(x - (crest - BeatDepth), BeatHalfWidth - Math.Abs(y - Layout.PilotPosition.Y));
        return MountainSurface.SmoothStep(inside / fade);
    }

    /// <summary>Horizontal distance to the chairlift's line.</summary>
    public static double LiftDistance(double x, double y)
    {
        double ex = LiftTop.X - LiftBottom.X, ey = LiftTop.Y - LiftBottom.Y;
        double t = Math.Clamp(((x - LiftBottom.X) * ex + (y - LiftBottom.Y) * ey) / (ex * ex + ey * ey), 0, 1);
        double dx = LiftBottom.X + t * ex - x, dy = LiftBottom.Y + t * ey - y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
