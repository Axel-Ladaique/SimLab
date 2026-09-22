using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Flight.Sim;

namespace Symlab.Flight.Tests.Behavior;

public class FlightQualityTests
{
    static Simulation Trimmed(string id)
    {
        var (speed, throttle) = Fleet.Cruise(id);
        var sim = Fleet.InFlight(id, 150, speed);
        Fleet.Fly(sim, 20, _ => new ControlInputs(throttle, 0, 0, 0));
        return sim;
    }

    static ControlInputs Cruise(string id) => new(Fleet.Cruise(id).Throttle, 0, 0, 0);

    [Fact]
    public void Trainer_flies_hands_off_for_thirty_seconds()
    {
        var sim = Fleet.InFlight("trainer", 100, Fleet.Cruise("trainer").Airspeed);
        double minAltitude = double.MaxValue, maxAltitude = double.MinValue, maxBank = 0;
        Fleet.Fly(sim, 30, _ => Cruise("trainer"), s =>
        {
            minAltitude = Math.Min(minAltitude, s.Aircraft.State.Position.Y);
            maxAltitude = Math.Max(maxAltitude, s.Aircraft.State.Position.Y);
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
        Assert.True(Math.Abs(sim.Aircraft.State.AngularVelocity.Z) < 0.1, $"pitch rate {sim.Aircraft.State.AngularVelocity.Z:F3}");
    }

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    public void Dutch_roll_damps_after_a_rudder_pulse(string id)
    {
        var sim = Trimmed(id);
        double start = sim.Time;
        Fleet.Fly(sim, 6.3, t => t - start < 0.3 ? Cruise(id) with { Rudder = 0.5 } : Cruise(id));
        Assert.True(Math.Abs(sim.Aircraft.State.AngularVelocity.Y) < 0.1, $"yaw rate {sim.Aircraft.State.AngularVelocity.Y:F3}");
    }

    [Fact]
    public void Trainer_spiral_mode_is_not_rapidly_divergent()
    {
        var sim = Trimmed("trainer");
        var s0 = sim.Aircraft.State;
        var banked = s0.Orientation * Quat.FromAxisAngle(Vec3.UnitX, Angle.Rad(20));
        sim.Aircraft.OverrideState(s0 with { Orientation = banked });
        Fleet.Fly(sim, 10, _ => Cruise("trainer"));
        Assert.True(Math.Abs(Fleet.Roll(sim)) < Angle.Rad(60), $"bank {Angle.Deg(Fleet.Roll(sim)):F0} deg");
    }
}
