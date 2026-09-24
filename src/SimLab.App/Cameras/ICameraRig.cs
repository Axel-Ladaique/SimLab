using SimLab.Flight.Geometry;

namespace SimLab.App.Cameras;

public readonly record struct CameraPose(Vec3 Position, Vec3 LookAt, double VerticalFovDeg);

/// <param name="AircraftPosition">Interpolated aircraft CG position, world axes.</param>
/// <param name="AircraftSpan">Largest dimension of the aircraft (wingspan), m.</param>
public readonly record struct CameraContext(Vec3 AircraftPosition, Quat AircraftOrientation, double AircraftSpan);

/// <summary>A camera behaviour. Line-of-sight now; chase and FPV rigs come in sub-project 3.</summary>
public interface ICameraRig
{
    CameraPose Update(double dt, in CameraContext ctx);
    void Reset(in CameraContext ctx);
}
