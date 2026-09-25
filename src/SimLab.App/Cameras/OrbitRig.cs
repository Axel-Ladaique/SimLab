using SimLab.Flight.Geometry;

namespace SimLab.App.Cameras;

/// <summary>Ground check camera: circles the aircraft at a fixed distance and height, scaled by its span.</summary>
public sealed class OrbitRig : ICameraRig
{
    public const double RadiusSpans = 1.6;
    public const double HeightSpans = 0.5;
    public const double DegreesPerSecond = 12;

    readonly double _fovDeg;
    double _angle;

    public OrbitRig(double fovDeg) => _fovDeg = fovDeg;

    public void Reset(in CameraContext ctx) => _angle = 0;

    public CameraPose Update(double dt, in CameraContext ctx)
    {
        _angle += DegreesPerSecond * Math.PI / 180 * dt;
        double r = RadiusSpans * ctx.AircraftSpan;
        var offset = new Vec3(r * Math.Cos(_angle), r * Math.Sin(_angle), HeightSpans * ctx.AircraftSpan);
        return new CameraPose(ctx.AircraftPosition + offset, ctx.AircraftPosition, _fovDeg);
    }
}
