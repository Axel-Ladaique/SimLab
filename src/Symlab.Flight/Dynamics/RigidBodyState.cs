using Symlab.Flight.Geometry;

namespace Symlab.Flight.Dynamics;

/// <summary>Position and velocity in world axes; orientation maps body to world; angular velocity in body axes.</summary>
public readonly record struct RigidBodyState(Vec3 Position, Vec3 Velocity, Quat Orientation, Vec3 AngularVelocity);

/// <summary>Force and moment about the CG, both in body axes.</summary>
public readonly record struct BodyLoad(Vec3 Force, Vec3 Moment)
{
    public static readonly BodyLoad Zero = new(Vec3.Zero, Vec3.Zero);
    public static BodyLoad operator +(BodyLoad a, BodyLoad b) => new(a.Force + b.Force, a.Moment + b.Moment);
}

public readonly record struct Wrench(Vec3 ForceWorld, Vec3 TorqueBody);

public delegate Wrench WrenchFunction(in RigidBodyState state);
