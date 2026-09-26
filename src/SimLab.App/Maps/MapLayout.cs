using SimLab.Flight.Geometry;

namespace SimLab.App.Maps;

/// <summary>
/// Where things are on a map (world ENU). The runway is a rectangle centred on <see cref="RunwayCentre"/>, its long
/// axis along <see cref="RunwayHeadingDeg"/> (compass, either direction); aircraft take off whichever way faces the
/// wind most directly.
/// </summary>
public sealed record MapLayout(
    Vec3 PilotPosition, double EyeHeight, Vec3 WindsockPosition,
    Vec3 RunwayCentre, double RunwayLength, double RunwayWidth, double RunwayHeadingDeg)
{
    const double TakeoffInset = 8;
    const double HandLaunchDistance = 3;

    /// <summary><see cref="PlanarYaw"/> of a frame whose local x runs along the runway toward its heading.</summary>
    double RunwayYaw => RunwayHeadingDeg - 90;

    public bool OnRunway(double x, double y)
    {
        var (along, across) = PlanarYaw.ToLocal(x - RunwayCentre.X, y - RunwayCentre.Y, RunwayYaw);
        return Math.Abs(along) <= RunwayLength / 2 && Math.Abs(across) <= RunwayWidth / 2;
    }

    /// <summary>The runway direction (the heading or its opposite, 0–360) that points most directly into the wind;
    /// the heading itself on a tie.</summary>
    public double TakeoffHeading(double windFromDeg)
    {
        double a = Normalize(RunwayHeadingDeg), b = Normalize(RunwayHeadingDeg + 180);
        return AngleBetween(windFromDeg, a) <= AngleBetween(windFromDeg, b) ? a : b;
    }

    /// <summary>On the centre line, <see cref="TakeoffInset"/> inside the threshold behind an aircraft taking off
    /// on <paramref name="headingDeg"/>.</summary>
    public (double X, double Y) TakeoffPoint(double headingDeg)
    {
        double h = Angle.Rad(headingDeg), back = RunwayLength / 2 - TakeoffInset;
        return (RunwayCentre.X - Math.Sin(h) * back, RunwayCentre.Y - Math.Cos(h) * back);
    }

    /// <summary><see cref="HandLaunchDistance"/> from the pilot toward the runway centre line, with the takeoff heading.</summary>
    public (double X, double Y, double HeadingDeg) HandLaunchPoint(double windFromDeg)
    {
        double h = Angle.Rad(RunwayHeadingDeg);
        double nx = Math.Cos(h), ny = -Math.Sin(h);
        if ((RunwayCentre.X - PilotPosition.X) * nx + (RunwayCentre.Y - PilotPosition.Y) * ny < 0) (nx, ny) = (-nx, -ny);
        return (PilotPosition.X + nx * HandLaunchDistance, PilotPosition.Y + ny * HandLaunchDistance, TakeoffHeading(windFromDeg));
    }

    static double Normalize(double deg) => (deg % 360 + 360) % 360;

    static double AngleBetween(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 360));
}
