using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;

namespace SimLab.Flight.Tests.Behavior;

public class GroundHandlingTests
{
    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("3d")]
    [InlineData("jet")]
    [InlineData("p51")]
    [InlineData("f18")]
    public void Rests_on_its_gear_without_drifting(string id)
    {
        var sim = Fleet.OnGround(id);
        Fleet.Fly(sim, 5, _ => ControlInputs.Neutral);
        var settled = sim.Aircraft.State;
        Fleet.Fly(sim, 15, _ => ControlInputs.Neutral);
        var s = sim.Aircraft.State;
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True((s.Position - settled.Position).Length < 0.05, $"drift {(s.Position - settled.Position).Length:F3} m");
        Assert.True(s.AngularVelocity.Length < 0.02);
        Assert.Equal(sim.Aircraft.Definition.Wheels.Count, sim.Aircraft.Ground.WheelsInContact(s, sim.Environment.Terrain));
    }

    [Fact]
    public void Trainer_takes_off_within_a_normal_ground_roll()
    {
        var sim = Fleet.OnGround("trainer");
        Fleet.Fly(sim, 1, _ => ControlInputs.Neutral);
        double startNorth = sim.Aircraft.State.Position.Y;
        double? liftOff = null;
        // Pitch-attitude pilot: hold 15 deg nose-up after t = 4 s (Kp = 2.0 per rad, Kd = 0.3 per rad/s).
        double targetPitch = Angle.Rad(15);
        const double kp = 2.0, kd = 0.3;
        ControlInputs Pilot(double t)
        {
            if (t <= 4) return new ControlInputs(1, 0, 0, 0);
            var st = sim.Aircraft.State;
            double pitch = Attitude.FromOrientation(st.Orientation).Pitch;
            double elevator = Math.Clamp(kp * (targetPitch - pitch) - kd * st.AngularVelocity.Y, -1, 1);
            return new ControlInputs(1, 0, elevator, 0);
        }
        Fleet.Fly(sim, 14, Pilot, s =>
        {
            if (liftOff is null && s.Aircraft.Ground.WheelsInContact(s.Aircraft.State, s.Environment.Terrain) == 0)
                liftOff = s.Aircraft.State.Position.Y - startNorth;
        });
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.NotNull(liftOff);
        Assert.InRange(liftOff!.Value, 5, 80);
        Assert.True(sim.Aircraft.State.Position.Z > 10, $"altitude {sim.Aircraft.State.Position.Z:F1} m");
    }
}
