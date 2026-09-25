using SimLab.Flight.Geometry;

namespace SimLab.App.Cameras;

/// <param name="Up">Camera up direction, world ENU axes (world up except in views that roll with the aircraft).</param>
public readonly record struct CameraPose(Vec3 Position, Vec3 LookAt, double VerticalFovDeg, Vec3 Up);

/// <param name="AircraftPosition">Interpolated aircraft CG position, world ENU axes.</param>
/// <param name="AircraftOrientation">Interpolated attitude, body → world.</param>
/// <param name="AircraftSpan">Largest dimension of the aircraft (wingspan), m.</param>
/// <param name="TerrainHeight">Ground height (m) at an east, north position; null when there is no ground.</param>
/// <param name="Aspect">Viewport width / height.</param>
public readonly record struct CameraContext(
    Vec3 AircraftPosition,
    Quat AircraftOrientation,
    double AircraftSpan,
    Func<double, double, double>? TerrainHeight = null,
    double Aspect = 16.0 / 9.0)
{
    public double GroundAt(double x, double y) => TerrainHeight?.Invoke(x, y) ?? double.NegativeInfinity;
}

/// <summary>A camera behaviour: line of sight from the ground, FPV or chase.</summary>
public interface ICameraRig
{
    CameraPose Update(double dt, in CameraContext ctx);
    void Reset(in CameraContext ctx);
}
