using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using SimLab.Flight.Sim;

namespace SimLab.Flight.Tests.Behavior;

public class FlightQualityTests
{
    static Simulation Trimmed(string id)
    {
        var sim = Fleet.InFlight(id, 150, Fleet.Cruise(id).Airspeed);
        Fleet.Fly(sim, 20, _ => Fleet.CruiseInputs(id));
        return sim;
    }

    static ControlInputs Cruise(string id) => Fleet.CruiseInputs(id);

    [Fact]
    public void Trainer_flies_hands_off_for_thirty_seconds()
    {
        var sim = Fleet.InFlight("trainer", 100, Fleet.Cruise("trainer").Airspeed);
        double minAltitude = double.MaxValue, maxAltitude = double.MinValue, maxBank = 0;
        Fleet.Fly(sim, 30, _ => Cruise("trainer"), s =>
        {
            minAltitude = Math.Min(minAltitude, s.Aircraft.State.Position.Z);
            maxAltitude = Math.Max(maxAltitude, s.Aircraft.State.Position.Z);
            maxBank = Math.Max(maxBank, Math.Abs(Fleet.Roll(s)));
        });
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.InRange(minAltitude, 40, 250);
        Assert.InRange(maxAltitude, 40, 250);
        Assert.InRange(sim.Aircraft.AirData.Airspeed, 8, 30);
        Assert.True(maxBank < Angle.Rad(45), $"max bank {Angle.Deg(maxBank):F0} deg");
    }

    [Theory]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    [InlineData("jet")]
    public void Flies_hands_off_for_ten_seconds(string id)
    {
        var sim = Fleet.InFlight(id, 100, Fleet.Cruise(id).Airspeed);
        Fleet.Fly(sim, 10, _ => Cruise(id));
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.InRange(sim.Aircraft.AirData.Airspeed, 8, 35);
    }

    [Fact]
    public void Trainer_phugoid_period_is_plausible()
    {
        var sim = Trimmed("trainer");
        var s0 = sim.Aircraft.State;
        sim.Aircraft.OverrideState(s0 with { Velocity = s0.Velocity * 1.2 });

        var samples = new List<(double T, double V)>();
        int step = 0;
        Fleet.Fly(sim, 60, _ => Cruise("trainer"), s =>
        {
            if (step++ % 50 == 0) samples.Add((s.Time, s.Aircraft.AirData.Airspeed));
        });

        double mean = samples.Average(p => p.V);
        var crossings = new List<double>();
        bool armed = false;
        foreach (var (t, v) in samples)
        {
            if (v < mean - 0.1) armed = true;
            else if (armed && v > mean + 0.1) { crossings.Add(t); armed = false; }
        }
        Assert.True(crossings.Count >= 2, $"only {crossings.Count} upward crossings");
        double period = (crossings[^1] - crossings[0]) / (crossings.Count - 1);
        Assert.InRange(period, 3, 15);
    }

    [Fact]
    public void Trainer_short_period_damps_quickly()
    {
        var sim = Trimmed("trainer");
        double start = sim.Time;
        Fleet.Fly(sim, 3.3, t => t - start < 0.3 ? Cruise("trainer") with { Elevator = 0.5 } : Cruise("trainer"));
        Assert.True(Math.Abs(sim.Aircraft.State.AngularVelocity.Y) < 0.1, $"pitch rate {sim.Aircraft.State.AngularVelocity.Y:F3}");
    }

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("3d")]
    [InlineData("jet")]
    public void Dutch_roll_damps_after_a_rudder_pulse(string id)
    {
        double Run(bool pulse)
        {
            var sim = Trimmed(id);
            double start = sim.Time;
            Fleet.Fly(sim, 6.3, t => pulse && t - start < 0.3 ? Cruise(id) with { Rudder = 0.5 } : Cruise(id));
            return sim.Aircraft.State.AngularVelocity.Z;
        }
        double residual = Run(true) - Run(false);
        Assert.True(Math.Abs(residual) < 0.1, $"yaw rate vs baseline {residual:F3}");
    }

    [Fact]
    public void Trainer_spiral_mode_is_not_rapidly_divergent()
    {
        var sim = Trimmed("trainer");
        var s0 = sim.Aircraft.State;
        var banked = s0.Orientation * Quat.FromAxisAngle(BodyAxes.Forward, Angle.Rad(20));
        sim.Aircraft.OverrideState(s0 with { Orientation = banked });
        Fleet.Fly(sim, 10, _ => Cruise("trainer"));
        Assert.True(Math.Abs(Fleet.Roll(sim)) < Angle.Rad(60), $"bank {Angle.Deg(Fleet.Roll(sim)):F0} deg");
    }
}
