using SimLab.Flight.Airframe;
using SimLab.Flight.Geometry;

namespace SimLab.App.Cameras;

/// <summary>Onboard FPV camera: rigidly fixed to the aircraft at its mount, tilted up, rolling with it. No lag.</summary>
public sealed class FpvRig : ICameraRig
{
    readonly FpvCameraSpec _mount;
    readonly Vec3 _lookBody;
    readonly Vec3 _upBody;

    public FpvRig(FpvCameraSpec mount)
    {
        _mount = mount;
        double tilt = Angle.Rad(mount.UptiltDeg);
        // Body forward (−x) turned up toward +z by the uptilt; up is +z leaned back (+x) by the same angle.
        _lookBody = new Vec3(-Math.Cos(tilt), 0, Math.Sin(tilt));
        _upBody = new Vec3(Math.Sin(tilt), 0, Math.Cos(tilt));
    }

    public void Reset(in CameraContext ctx) { }

    public CameraPose Update(double dt, in CameraContext ctx)
    {
        var q = ctx.AircraftOrientation;
        var eye = ctx.AircraftPosition + q.Rotate(_mount.Position);
        return new CameraPose(eye, eye + q.Rotate(_lookBody), VerticalFov(_mount.HorizontalFovDeg, ctx.Aspect), q.Rotate(_upBody));
    }

    /// <summary>Vertical field of view showing <paramref name="horizontalFovDeg"/> across a screen of that aspect.</summary>
    public static double VerticalFov(double horizontalFovDeg, double aspect) =>
        Angle.Deg(2 * Math.Atan(Math.Tan(Angle.Rad(horizontalFovDeg) / 2) / Math.Max(aspect, 0.1)));
}
