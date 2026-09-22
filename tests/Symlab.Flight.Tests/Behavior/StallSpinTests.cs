using Symlab.Flight.Airframe;
using Symlab.Flight.Controls;
using Symlab.Flight.Ground;

namespace Symlab.Flight.Tests.Behavior;

public class StallSpinTests
{
    [Fact]
    public void Trainer_minimum_speed_matches_its_wing_loading()
    {
        var sim = Fleet.InFlight("trainer", 150, 16);
        double minSpeed = double.MaxValue;
        Fleet.Fly(sim, 15, t => new ControlInputs(0, 0, Math.Min(1, t / 15), 0),
            s => minSpeed = Math.Min(minSpeed, s.Aircraft.AirData.Airspeed));

        var def = sim.Aircraft.Definition;
        double clMax3d = 0.9 * 1.28;
        double stallSpeed = Math.Sqrt(2 * def.Mass.Mass * Aircraft.Gravity / (1.225 * sim.Aircraft.Aero.WingArea * clMax3d));
        Assert.InRange(minSpeed, 0.7 * stallSpeed, 1.35 * stallSpeed);
    }

    [Fact]
    public void Sport_spins_with_pro_spin_controls_and_recovers_when_released()
    {
        var sim = Fleet.InFlight("sport", 250, 14);
        Fleet.Fly(sim, 1, t => new ControlInputs(0, 0, Math.Min(1, t), 0));
        var rates = new List<double>();
        double spinStart = sim.Time;
        Fleet.Fly(sim, 6, _ => new ControlInputs(0, 0, 1, 1), s =>
        {
            if (s.Time - spinStart > 4) rates.Add(Fleet.WorldYawRightRate(s));
        });
        Assert.True(rates.Average() > 1.5, $"spin rate {rates.Average():F2} rad/s");

        Fleet.Fly(sim, 6, _ => ControlInputs.Neutral);
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(Math.Abs(Fleet.WorldYawRightRate(sim)) < 0.8, $"residual yaw rate {Fleet.WorldYawRightRate(sim):F2}");
    }

    [Fact]
    public void Trainer_recovers_hands_off_from_an_incipient_spin()
    {
        var sim = Fleet.InFlight("trainer", 250, 14);
        Fleet.Fly(sim, 1, t => new ControlInputs(0, 0, Math.Min(1, t), 0));
        Fleet.Fly(sim, 4, _ => new ControlInputs(0, 0, 1, 1));
        Fleet.Fly(sim, 5, _ => ControlInputs.Neutral);
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(Math.Abs(Fleet.WorldYawRightRate(sim)) < 0.5, $"residual yaw rate {Fleet.WorldYawRightRate(sim):F2}");
    }
}
