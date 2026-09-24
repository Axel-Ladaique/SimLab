using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests.Dynamics;

public class Rk4IntegratorTests
{
    const double Dt = 0.002;

    static RigidBodyState Run(RigidBodyState s, MassProperties m, WrenchFunction f, double seconds, double dt = Dt)
    {
        int n = (int)Math.Round(seconds / dt);
        for (int i = 0; i < n; i++) s = Rk4Integrator.Step(s, dt, m, f);
        return s;
    }

    static RigidBodyState AtRest => new(Vec3.Zero, Vec3.Zero, Quat.Identity, Vec3.Zero);

    [Fact]
    public void Free_fall_matches_analytic_solution()
    {
        var m = MassProperties.FromPrincipal(2, 1, 1, 1);
        var s = Run(AtRest, m, (in RigidBodyState _) => new Wrench(new Vec3(0, -2 * 9.81, 0), Vec3.Zero), 2.0);
        Assert.Equal(-0.5 * 9.81 * 4, s.Position.Y, 9);
        Assert.Equal(-9.81 * 2, s.Velocity.Y, 9);
    }

    [Fact]
    public void Torque_free_rotation_conserves_energy_and_angular_momentum()
    {
        var m = MassProperties.FromPrincipal(1, 0.1, 0.3, 0.2);
        var s0 = AtRest with { AngularVelocity = new Vec3(0.3, 5, 0.2) };
        static double Energy(RigidBodyState s, MassProperties m) => 0.5 * Vec3.Dot(s.AngularVelocity, m.Inertia * s.AngularVelocity);
        static Vec3 MomentumWorld(RigidBodyState s, MassProperties m) => s.Orientation.Rotate(m.Inertia * s.AngularVelocity);

        var s1 = Run(s0, m, (in RigidBodyState _) => new Wrench(Vec3.Zero, Vec3.Zero), 10.0);

        Assert.Equal(1.0, Energy(s1, m) / Energy(s0, m), 6);
        var l0 = MomentumWorld(s0, m);
        var l1 = MomentumWorld(s1, m);
        Assert.True((l1 - l0).Length / l0.Length < 1e-6, $"angular momentum drift {(l1 - l0).Length}");
        Assert.Equal(1.0, s1.Orientation.Length, 12);
    }

    [Fact]
    public void Constant_pitch_torque_gives_uniform_angular_acceleration()
    {
        var m = MassProperties.FromPrincipal(1, 0.1, 0.1, 0.5);
        var s = Run(AtRest, m, (in RigidBodyState _) => new Wrench(Vec3.Zero, new Vec3(0, 0, 0.5)), 1.0);
        Assert.Equal(1.0, s.AngularVelocity.Z, 9);
        var nose = s.Orientation.Rotate(Vec3.UnitX);
        Assert.Equal(Math.Cos(0.5), nose.X, 9);
        Assert.Equal(Math.Sin(0.5), nose.Y, 9);
    }

    [Fact]
    public void Error_shrinks_at_fourth_order()
    {
        var m = MassProperties.FromPrincipal(1, 0.1, 0.3, 0.2);
        var s0 = AtRest with { AngularVelocity = new Vec3(2, 1, 3) };
        WrenchFunction f = (in RigidBodyState _) => new Wrench(Vec3.Zero, Vec3.Zero);
        var reference = Run(s0, m, f, 2.0, 0.02 / 16).Orientation.Rotate(Vec3.UnitX);
        var coarse = Run(s0, m, f, 2.0, 0.02).Orientation.Rotate(Vec3.UnitX);
        var fine = Run(s0, m, f, 2.0, 0.01).Orientation.Rotate(Vec3.UnitX);
        var ratio = (coarse - reference).Length / (fine - reference).Length;
        Assert.True(ratio > 10, $"error ratio {ratio}");
    }
}
