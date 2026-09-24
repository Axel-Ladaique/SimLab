using SimLab.Flight.Geometry;

namespace SimLab.App.Field;

/// <summary>Layout of the generic club field. The runway runs east-west, centered on the origin; the pilot box is south of it.</summary>
public static class ClubField
{
    public const double RunwayLength = 100;
    public const double RunwayWidth = 15;
    public const double FlatRadius = 300;
    public const double BlendWidth = 300;
    public const double HillAmplitude = 8;
    public const double EyeHeight = 1.7;
    public const double TerrainHalfSize = 1000;
    const double TakeoffInset = 8;
    const double HandLaunchDistance = 3;

    public static readonly Vec3 PilotPosition = new(0, 0, 25);
    public static readonly Vec3 WindsockPosition = new(20, 0, 28);

    public static bool OnRunway(double x, double z) => Math.Abs(x) <= RunwayLength / 2 && Math.Abs(z) <= RunwayWidth / 2;

    /// <summary>Runway heading (90 = toward east, 270 = toward west) that points most directly into the wind.</summary>
    public static double TakeoffHeading(double windFromDeg) =>
        AngleBetween(windFromDeg, 90) <= AngleBetween(windFromDeg, 270) ? 90 : 270;

    public static (double X, double Z) TakeoffPoint(double headingDeg) =>
        headingDeg == 90 ? (-RunwayLength / 2 + TakeoffInset, 0) : (RunwayLength / 2 - TakeoffInset, 0);

    public static (double X, double Z, double HeadingDeg) HandLaunchPoint(double windFromDeg) =>
        (PilotPosition.X, PilotPosition.Z - HandLaunchDistance, TakeoffHeading(windFromDeg));

    static double AngleBetween(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 360));
}
