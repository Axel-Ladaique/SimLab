using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using SimLab.Flight.Sim;

namespace SimLab.Flight.Tests.Behavior;

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
        Assert.True(sim.Aircraft.State.Position.Z < 0.2);
    }

    /// <summary>
    /// Hand-launch pilot: full throttle, holding a 15 degree climb attitude with the elevator (0.1 up plus 1 per radian of
    /// pitch error) and the wings level with the ailerons (1 per radian of bank), each limited to half throw — as a real
    /// pilot does against the motor torque and the zoom climb of a stable wing at full power.
    /// </summary>
    internal static ControlInputs HandLaunchPilot(Simulation sim)
    {
        var attitude = Attitude.FromOrientation(sim.Aircraft.State.Orientation);
        double elevator = Math.Clamp(0.1 + (Angle.Rad(15) - attitude.Pitch), -0.5, 0.5);
        double aileron = Math.Clamp(-attitude.Roll, -0.5, 0.5);
        return new ControlInputs(1, aileron, elevator, 0);
    }

    [Fact]
    public void Wing_hand_launch_climbs_away()
    {
        var sim = Fleet.InFlight("wing", 1.8, 10, pitchDeg: 10);
        Fleet.Fly(sim, 6, _ => HandLaunchPilot(sim));
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(sim.Aircraft.State.Position.Z > 5, $"altitude {sim.Aircraft.State.Position.Z:F1} m");
    }
}
