using SimLab.Flight.Controls;

namespace SimLab.Flight.Tests.Behavior;

public class BrakeTests
{
    [Theory]
    [InlineData("p51")]
    [InlineData("f18")]
    public void Idling_fuel_engine_does_not_creep_with_the_brakes_on(string id)
    {
        var sim = Fleet.OnGround(id);
        Fleet.Fly(sim, 3, _ => ControlInputs.Neutral);
        var start = sim.Aircraft.State.Position;
        Fleet.Fly(sim, 10, _ => ControlInputs.Neutral);
        Assert.True((sim.Aircraft.State.Position - start).Length < 0.05, $"crept {(sim.Aircraft.State.Position - start).Length:F2} m");
        Assert.Equal(1, sim.Aircraft.Ground.BrakeCommand);
    }

    [Theory]
    [InlineData("p51", 0.3)]
    [InlineData("f18", 0.4)]
    public void Opening_the_throttle_releases_the_brakes(string id, double throttle)
    {
        var sim = Fleet.OnGround(id);
        var start = sim.Aircraft.State.Position;
        Fleet.Fly(sim, 6, _ => new ControlInputs(throttle, 0, 0, 0));
        Assert.Equal(0, sim.Aircraft.Ground.BrakeCommand);
        Assert.True((sim.Aircraft.State.Position - start).Length > 2, $"rolled {(sim.Aircraft.State.Position - start).Length:F2} m");
    }
}
