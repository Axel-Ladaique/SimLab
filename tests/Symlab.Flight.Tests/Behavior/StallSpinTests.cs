using Symlab.Flight.Airframe;
using Symlab.Flight.Controls;
using Symlab.Flight.Ground;
using Symlab.Flight.Sim;

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

    const double StallAlphaDeg = 14;
    const double DevelopedSpinYawRate = 1.5;

    /// <summary>Flies the pro-spin phase and returns the mean alpha (deg) and mean world yaw-right rate (rad/s) over its last 1.5 s.</summary>
    static (double AlphaDeg, double YawRate) ProSpin(Simulation sim, double seconds)
    {
        double start = sim.Time;
        var alphas = new List<double>();
        var rates = new List<double>();
        Fleet.Fly(sim, seconds, _ => new ControlInputs(0, 0, 1, 1), s =>
        {
            if (s.Time - start > seconds - 1.5)
            {
                alphas.Add(s.Aircraft.AirData.Alpha * 180 / Math.PI);
                rates.Add(Fleet.WorldYawRightRate(s));
            }
        });
        return (alphas.Average(), rates.Average());
    }

    [Fact]
    public void Sport_spins_with_pro_spin_controls_and_recovers_with_standard_inputs()
    {
        var sim = Fleet.InFlight("sport", 250, 14);
        Fleet.Fly(sim, 1, t => new ControlInputs(0, 0, Math.Min(1, t), 0));
        var (alpha, rate) = ProSpin(sim, 6);
        Assert.True(alpha > StallAlphaDeg, $"spin alpha {alpha:F1} deg");
        Assert.True(rate > DevelopedSpinYawRate, $"spin rate {rate:F2} rad/s");

        // PARE: power idle, ailerons neutral, full opposite rudder, elevator neutral; then neutralize.
        Fleet.Fly(sim, 2, _ => new ControlInputs(0, 0, 0, -1));
        Fleet.Fly(sim, 4, _ => ControlInputs.Neutral);
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(Math.Abs(Fleet.WorldYawRightRate(sim)) < 0.8, $"residual yaw rate {Fleet.WorldYawRightRate(sim):F2}");
    }

    [Fact]
    public void Trainer_resists_spin_entry_and_recovers()
    {
        var sim = Fleet.InFlight("trainer", 250, 14);
        Fleet.Fly(sim, 1, t => new ControlInputs(0, 0, Math.Min(1, t), 0));
        var (alpha, rate) = ProSpin(sim, 4);
        Assert.True(alpha < StallAlphaDeg, $"alpha {alpha:F1} deg (developed spin)");
        Assert.True(rate < DevelopedSpinYawRate, $"yaw rate {rate:F2} rad/s");

        Fleet.Fly(sim, 5, _ => ControlInputs.Neutral);
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(Math.Abs(Fleet.WorldYawRightRate(sim)) < 0.5, $"residual yaw rate {Fleet.WorldYawRightRate(sim):F2}");
    }
}
