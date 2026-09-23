using Symlab.Flight.Geometry;

namespace Symlab.App.Cameras;

/// <summary>The pilot's eyes in the pilot box: follows the aircraft like a head, with optional limited auto-zoom.</summary>
public sealed class LineOfSightRig : ICameraRig
{
    public const double HeadTimeConstant = 0.06;
    public const double TargetScreenFraction = 0.08;
    public const double MaxZoomFactor = 3.0;

    Vec3 _look = new(0, 0, -1);
    bool _initialized;

    public LineOfSightRig(Vec3 eye, double baseFovDeg, bool autoZoom)
    {
        Eye = eye;
        BaseFovDeg = baseFovDeg;
        AutoZoom = autoZoom;
    }

    public Vec3 Eye { get; }
    public double BaseFovDeg { get; set; }
    public bool AutoZoom { get; set; }

    public void Reset(in CameraContext ctx)
    {
        var to = ctx.AircraftPosition - Eye;
        if (to.Length > 1e-6) _look = to.Normalized();
        _initialized = true;
    }

    public CameraPose Update(double dt, in CameraContext ctx)
    {
        if (!_initialized) Reset(ctx);
        var to = ctx.AircraftPosition - Eye;
        double distance = to.Length;
        var direction = distance > 1e-6 ? to / distance : _look;
        double k = 1 - Math.Exp(-Math.Max(dt, 0) / HeadTimeConstant);
        _look = (_look + (direction - _look) * k).Normalized();
        double fov = AutoZoom ? ZoomedFov(distance, ctx.AircraftSpan) : BaseFovDeg;
        return new CameraPose(Eye, Eye + _look * Math.Max(distance, 1.0), fov);
    }

    /// <summary>FOV that shows the span at <see cref="TargetScreenFraction"/> of the screen, never narrower than base/<see cref="MaxZoomFactor"/>.</summary>
    public double ZoomedFov(double distance, double span)
    {
        double apparent = Angle.Deg(2 * Math.Atan(span / (2 * Math.Max(distance, 0.1))));
        return Math.Clamp(apparent / TargetScreenFraction, BaseFovDeg / MaxZoomFactor, BaseFovDeg);
    }

    /// <summary>Vertical FOV giving true apparent size for a screen of that height seen from that distance.</summary>
    public static double FovForScreen(double screenHeightCm, double viewingDistanceCm) =>
        Angle.Deg(2 * Math.Atan(screenHeightCm / (2 * viewingDistanceCm)));
}
