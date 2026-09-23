using Symlab.Flight.Controls;
using Symlab.Flight.Ground;

namespace Symlab.Flight.Tests.Behavior;

public class GroundHandlingTests
{
    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
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
        double startNorth = -sim.Aircraft.State.Position.Z;
        double? liftOff = null;
        Fleet.Fly(sim, 14, t => new ControlInputs(1, 0, t > 5.5 ? 0.1 : t > 4 ? 0.35 : 0, 0), s =>
        {
            if (liftOff is null && s.Aircraft.Ground.WheelsInContact(s.Aircraft.State, s.Environment.Terrain) == 0)
                liftOff = -s.Aircraft.State.Position.Z - startNorth;
        });
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.NotNull(liftOff);
        Assert.InRange(liftOff!.Value, 5, 80);
        Assert.True(sim.Aircraft.State.Position.Y > 10, $"altitude {sim.Aircraft.State.Position.Y:F1} m");
    }
}
