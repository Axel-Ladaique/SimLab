using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Aero;

/// <summary>Propeller slipstream: points behind the disk and within <see cref="Radius"/> of its axis get <see cref="Velocity"/> added along the axis.</summary>
public readonly record struct PropWash(Vec3 PositionBody, Vec3 AxisBody, double Radius, double Velocity);

/// <param name="AirVelocityBody">CG velocity relative to the air mass, body axes.</param>
/// <param name="UpBody">World up expressed in body axes.</param>
/// <param name="Deflections">Control-surface deflections in radians (positive = trailing edge down), indexed like the model's control list.</param>
public readonly record struct AeroContext(
    Vec3 AirVelocityBody,
    Vec3 AngularVelocityBody,
    double Density,
    double HeightAboveGround,
    Vec3 UpBody,
    IReadOnlyList<double> Deflections,
    PropWash Wash);

public interface IAeroModel
{
    /// <summary>Total aerodynamic force and moment about the CG, body axes. May cache inputs used by Advance; must not advance lagged states.</summary>
    BodyLoad Evaluate(in AeroContext ctx);

    /// <summary>Advances internal lagged states (e.g. downwash) using the last evaluation.</summary>
    void Advance(double dt);

    void Reset();
}
