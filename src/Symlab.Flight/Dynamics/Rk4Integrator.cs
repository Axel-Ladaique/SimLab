using Symlab.Flight.Geometry;

namespace Symlab.Flight.Dynamics;

public static class Rk4Integrator
{
    readonly record struct Derivative(Vec3 Position, Vec3 Velocity, Quat Orientation, Vec3 AngularVelocity);

    public static RigidBodyState Step(in RigidBodyState s, double dt, MassProperties m, WrenchFunction f)
    {
        var k1 = Evaluate(s, m, f);
        var k2 = Evaluate(Apply(s, k1, dt / 2), m, f);
        var k3 = Evaluate(Apply(s, k2, dt / 2), m, f);
        var k4 = Evaluate(Apply(s, k3, dt), m, f);
        var h = dt / 6.0;
        return new RigidBodyState(
            s.Position + (k1.Position + 2 * k2.Position + 2 * k3.Position + k4.Position) * h,
            s.Velocity + (k1.Velocity + 2 * k2.Velocity + 2 * k3.Velocity + k4.Velocity) * h,
            (s.Orientation + (k1.Orientation + k2.Orientation * 2 + k3.Orientation * 2 + k4.Orientation) * h).Normalized(),
            s.AngularVelocity + (k1.AngularVelocity + 2 * k2.AngularVelocity + 2 * k3.AngularVelocity + k4.AngularVelocity) * h);
    }

    static Derivative Evaluate(in RigidBodyState s, MassProperties m, WrenchFunction f)
    {
        var w = f(s);
        var omega = s.AngularVelocity;
        var spin = new Quat(omega.X, omega.Y, omega.Z, 0);
        var angularAcceleration = m.InverseInertia * (w.TorqueBody - Vec3.Cross(omega, m.Inertia * omega));
        return new Derivative(s.Velocity, w.ForceWorld / m.Mass, (s.Orientation * spin) * 0.5, angularAcceleration);
    }

    static RigidBodyState Apply(in RigidBodyState s, in Derivative d, double h) => new(
        s.Position + d.Position * h,
        s.Velocity + d.Velocity * h,
        (s.Orientation + d.Orientation * h).Normalized(),
        s.AngularVelocity + d.AngularVelocity * h);
}
