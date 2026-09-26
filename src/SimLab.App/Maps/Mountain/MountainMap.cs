using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Mountain;

/// <summary>
/// The mountain slope site (world ENU, z = 0 at 1 500 m): a strip on the shoulder just east of a curved crest, the
/// pilot at its western edge facing a 350 m drop into a valley with a lake and a stream, a switchback road climbing
/// the face, and the ground rising east to a snowy summit. With wind from the west aircraft take off toward the drop.
/// </summary>
public static class MountainMap
{
    public const string Id = "mountain";
    public const int Seed = 11;
    public const double HalfSize = 2000, GridStep = 4, DatumElevationM = 1500, LakeLevel = -350;

    public static readonly MapLayout Layout = new(
        PilotPosition: new Vec3(0, -22, 0), EyeHeight: 1.7, WindsockPosition: new Vec3(12, -30, 0),
        RunwayCentre: new Vec3(95, 0, 0), RunwayLength: 130, RunwayWidth: 20, RunwayHeadingDeg: 90);

    /// <summary>A deeper blue sky and less haze than the lowland club.</summary>
    public static readonly MapAmbience Ambience = new(
        SkyTop: new Rgb(0.22f, 0.42f, 0.78f), SkyHorizon: new Rgb(0.62f, 0.74f, 0.88f),
        GroundHorizon: new Rgb(0.30f, 0.34f, 0.30f), GroundBottom: new Rgb(0.10f, 0.12f, 0.10f), FogDensity: 0.00005);

    public static FieldMap Create()
    {
        var road = new MountainRoad();
        return new FieldMap(Id, "FIELD_MOUNTAIN", MountainRelief.Build(road), (_, _) => SurfaceWeights.Only(SurfaceKind.Grass),
            Layout, Ambience, [], [], [MountainRelief.Lake])
        {
            DatumElevationM = DatumElevationM,
        };
    }
}
