using Symlab.Flight.Controls;
using Symlab.Flight.Ground;

namespace Symlab.Flight.Tests.Behavior;

public class LandingAndCrashTests
{
    [Fact]
    public void Steep_dive_into_the_ground_is_a_crash()
    {
        var sim = Fleet.InFlight("trainer", 5, 20, pitchDeg: -60);
        Fleet.Fly(sim, 2, _ => ControlInputs.Neutral);
        Assert.NotEqual(CrashCause.None, sim.Aircraft.Crash);
    }

    [Fact]
    public void Wing_belly_lands_and_slides_to_a_stop()
    {
        var sim = Fleet.InFlight("wing", 1.5, 11);
        Fleet.Fly(sim, 10, _ => new ControlInputs(0, 0, 0.2, 0));
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(sim.Aircraft.State.Velocity.Length < 1.0, $"speed {sim.Aircraft.State.Velocity.Length:F2}");
        Assert.True(sim.Aircraft.State.Position.Y < 0.2);
    }

    [Fact]
    public void Wing_hand_launch_climbs_away()
    {
        var sim = Fleet.InFlight("wing", 1.8, 10, pitchDeg: 10);
        Fleet.Fly(sim, 6, _ => new ControlInputs(1, 0, 0.1, 0));
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(sim.Aircraft.State.Position.Y > 5, $"altitude {sim.Aircraft.State.Position.Y:F1} m");
    }
}
