using SimLab.Flight.Geometry;

namespace SimLab.App.Cameras;

/// <summary>
/// Third-person chase camera: behind and above the aircraft along its smoothed horizontal heading, looking at it with
/// a level horizon (it never rolls or pitches with the aircraft), and never below the ground.
/// </summary>
public sealed class ChaseRig : ICameraRig
{
    public const double DistanceSpans = 3;
    public const double HeightSpans = 0.8;
    public const double HeadingTimeConstant = 0.4;
    public const double GroundClearance = 0.5;
    public const double FovDeg = 60;
    /// <summary>Within this angle of vertical the nose gives no heading, and the last one is kept.</summary>
    public const double VerticalLimitDeg = 10;

    /// <summary>Math angle of the heading in the horizontal plane (rad, from east toward north).</summary>
    double _heading;
    bool _initialized;

    public void Reset(in CameraContext ctx)
    {
        _heading = NoseHeading(ctx.AircraftOrientation)
            ?? (_initialized ? _heading : Horizontal(-ctx.AircraftOrientation.Rotate(BodyAxes.Up)) ?? 0);
        _initialized = true;
    }

    public CameraPose Update(double dt, in CameraContext ctx)
    {
        if (!_initialized) Reset(ctx);
        if (NoseHeading(ctx.AircraftOrientation) is { } target)
        {
            double k = 1 - Math.Exp(-Math.Max(dt, 0) / HeadingTimeConstant);
            _heading += Math.IEEERemainder(target - _heading, 2 * Math.PI) * k;
        }
        var back = new Vec3(-Math.Cos(_heading), -Math.Sin(_heading), 0);
        var p = ctx.AircraftPosition;
        var eye = p + back * (DistanceSpans * ctx.AircraftSpan) + Vec3.UnitZ * (HeightSpans * ctx.AircraftSpan);
        double floor = ctx.GroundAt(eye.X, eye.Y) + GroundClearance;
        if (eye.Z < floor) eye = eye with { Z = floor };
        return new CameraPose(eye, p, FovDeg, Vec3.UnitZ);
    }

    /// <summary>Math heading of the nose, or null when it points within <see cref="VerticalLimitDeg"/> of vertical.</summary>
    static double? NoseHeading(Quat orientation) => Horizontal(orientation.Rotate(BodyAxes.Forward));

    static double? Horizontal(Vec3 v) =>
        Math.Sqrt(v.X * v.X + v.Y * v.Y) < Math.Sin(Angle.Rad(VerticalLimitDeg)) * v.Length ? null : Math.Atan2(v.Y, v.X);
}
