using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

namespace SimLab.App.Mapping;

public static class StateInterpolation
{
    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;

    /// <summary>Normalized linear quaternion interpolation along the shorter arc (accurate for 2 ms steps).</summary>
    public static Quat Nlerp(Quat a, Quat b, double t)
    {
        double dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        if (dot < 0) b = b * -1.0;
        return (a * (1 - t) + b * t).Normalized();
    }

    public static RigidBodyState Interpolate(in RigidBodyState a, in RigidBodyState b, double t) => new(
        Lerp(a.Position, b.Position, t),
        Lerp(a.Velocity, b.Velocity, t),
        Nlerp(a.Orientation, b.Orientation, t),
        Lerp(a.AngularVelocity, b.AngularVelocity, t));
}
