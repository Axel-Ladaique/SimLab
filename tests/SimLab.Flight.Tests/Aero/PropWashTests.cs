using SimLab.Flight.Aero;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests.Aero;

/// <summary>The propeller slipstream field: momentum theory near the disk, mixing downstream, swirl from the torque.</summary>
public class PropWashTests
{
    const double Rho = 1.225;
    const double R = 0.1524;        // 12 in prop
    const double Thrust = 21.6;     // sport hover
    const double Torque = 0.42;

    static PropWash Wash(double axialSpeed = 0, double thrust = Thrust, double torque = Torque, int spin = 1) =>
        PropWash.Create(Vec3.Zero, BodyAxes.Forward, R, thrust, torque, axialSpeed, Rho, spin);

    static double StaticInducedVelocity(double thrust = Thrust) => Math.Sqrt(thrust / (2 * Rho * Math.PI * R * R));

    /// <summary>Point <paramref name="distance"/> behind the disk (body +x for a forward thrust axis), offset by <paramref name="radial"/>.</summary>
    static Vec3 Behind(double distance, Vec3 radial = default) => new Vec3(distance, 0, 0) + radial;

    /// <summary>Axial slipstream velocity (m/s, positive = air pushed aft) at a point.</summary>
    static double Axial(PropWash w, Vec3 p) => Vec3.Dot(w.AirVelocityAt(p), -BodyAxes.Forward);

    /// <summary>Midpoint-rule integral over the plane <paramref name="distance"/> behind the disk of f(r, axial, swirl) 2πr dr.</summary>
    static double Integrate(PropWash w, double distance, Func<double, double, double, double> f)
    {
        var st = w.At(distance);
        double outer = st.OuterRadius, sum = 0;
        const int n = 4000;
        for (int i = 0; i < n; i++)
        {
            double r = (i + 0.5) / n * outer;
            sum += f(r, st.AxialVelocity(r), st.SwirlVelocity(r)) * 2 * Math.PI * r * outer / n;
        }
        return sum;
    }

    [Fact]
    public void Velocity_at_the_disk_is_the_momentum_theory_induced_velocity()
    {
        var w = Wash();
        Assert.Equal(StaticInducedVelocity(), w.InducedVelocity, 6);
        Assert.Equal(StaticInducedVelocity(), Axial(w, Behind(1e-6)), 2);
    }

    [Fact]
    public void Nothing_ahead_of_the_disk_or_without_thrust()
    {
        Assert.Equal(Vec3.Zero, Wash().AirVelocityAt(Behind(-0.05)));
        Assert.Equal(Vec3.Zero, Wash(thrust: 0).AirVelocityAt(Behind(0.3)));
        Assert.Equal(Vec3.Zero, default(PropWash).AirVelocityAt(Behind(0.3)));
    }

    [Fact]
    public void Slipstream_accelerates_to_twice_the_induced_velocity_and_contracts_within_about_one_radius()
    {
        var w = Wash();
        double vi = StaticInducedVelocity();
        Assert.InRange(Axial(w, Behind(R)) / vi, 1.65, 1.75);                    // 1 + 1/√2
        Assert.InRange(Axial(w, Behind(3 * R)) / vi, 1.9, 2.0);
        Assert.InRange(w.At(R).TubeRadius / R, 0.74, 0.79);                        // √(1/1.707)
        Assert.InRange(w.At(3 * R).TubeRadius / (R / Math.Sqrt(2)), 1.0, 1.02);
        Assert.True(w.At(1.5 * R).HalfVelocityRadius < w.At(0.01 * R).HalfVelocityRadius, "the jet contracts behind the disk");
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(0, 8)]
    [InlineData(0, 20)]
    [InlineData(10, 3)]
    [InlineData(10, 20)]
    public void Momentum_flux_carries_the_thrust_once_the_pressure_has_recovered(double axialSpeed, double radii)
    {
        var w = Wash(axialSpeed);
        double flux = Integrate(w, radii * R, (_, u, _) => Rho * (axialSpeed + u) * u);
        // The ideal contraction is 97-99.5% complete at 3 R (the rest is still carried by the pressure field).
        Assert.InRange(flux / Thrust, 0.96, 1.01);
    }

    [Fact]
    public void Radial_profile_is_smooth_and_decreases_outward()
    {
        var w = Wash();
        foreach (double s in new[] { 0.01 * R, R, 3 * R, 8 * R, 20 * R })
        {
            var st = w.At(s);
            double previous = st.AxialVelocity(0);
            for (double r = 0.0005; r < st.OuterRadius + 0.01; r += 0.0005)
            {
                double u = st.AxialVelocity(r);
                Assert.True(u <= previous + 1e-12, $"not decreasing at s {s:F3} r {r:F4}");
                Assert.True(previous - u < 0.03 * st.CentreVelocity, $"jump at s {s:F3} r {r:F4}: {previous:F2} → {u:F2}");
                previous = u;
            }
            Assert.Equal(0, st.AxialVelocity(st.OuterRadius + 0.001));
        }
    }

    [Fact]
    public void Slipstream_decays_and_spreads_downstream()
    {
        var w = Wash();
        var near = w.At(6 * R);
        var far = w.At(20 * R);
        Assert.True(far.CentreVelocity < 0.8 * near.CentreVelocity, $"{far.CentreVelocity:F2} vs {near.CentreVelocity:F2}");
        Assert.True(far.OuterRadius > near.OuterRadius);
    }

    [Fact]
    public void Wash_is_continuous_in_airspeed_and_small_at_high_advance_ratio()
    {
        var p = Behind(4 * R, new Vec3(0, 0.03, 0));
        Assert.Equal(Axial(Wash(0), p), Axial(Wash(1e-4), p), 2);
        // Cruise: 2 N of thrust at 18 m/s adds only a few m/s.
        double cruise = Axial(Wash(18, thrust: 2, torque: 0.05), Behind(4 * R));
        Assert.InRange(cruise, 0.5, 3.0);
        double faster = Axial(Wash(30, thrust: 2, torque: 0.05), Behind(4 * R));
        Assert.True(faster < cruise);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Swirl_turns_with_the_prop(int spin)
    {
        // Clockwise seen from behind (+1): the air above the axis moves right (+y).
        var w = Wash(spin: spin);
        var above = w.AirVelocityAt(Behind(3 * R, new Vec3(0, 0, 0.05)));
        var right = w.AirVelocityAt(Behind(3 * R, new Vec3(0, 0.05, 0)));
        Assert.Equal(spin, Math.Sign(above.Y));
        Assert.Equal(-spin, Math.Sign(right.Z));
        Assert.Equal(0, above.Z, 9);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(0, 10)]
    [InlineData(10, 3)]
    public void Swirl_carries_the_calibrated_share_of_the_torque_as_angular_momentum_flux(double axialSpeed, double radii)
    {
        var w = Wash(axialSpeed);
        double flux = Integrate(w, radii * R, (r, u, swirl) => Rho * (axialSpeed + u) * r * swirl);
        Assert.Equal(PropWash.SwirlEfficiency * Torque, flux, 3);
        Assert.Equal(0, Wash(axialSpeed, torque: 0).At(radii * R).SwirlRate);
    }
}
