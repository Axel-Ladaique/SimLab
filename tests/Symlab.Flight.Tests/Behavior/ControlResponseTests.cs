using Symlab.Flight.Controls;
using Symlab.Flight.Sim;

namespace Symlab.Flight.Tests.Behavior;

public class ControlResponseTests
{
    static void AssertResponse(string id, Func<ControlInputs, ControlInputs> apply, Func<Simulation, double> measure, double minimum)
    {
        var (speed, throttle) = Fleet.Cruise(id);
        double Run(bool withInput)
        {
            var sim = Fleet.InFlight(id, 80, speed);
            var trim = new ControlInputs(throttle, 0, 0, 0);
            Fleet.Fly(sim, 1.0, _ => trim);
            Fleet.Fly(sim, 0.4, _ => withInput ? apply(trim) : trim);
            return measure(sim);
        }
        double response = Run(true) - Run(false);
        Assert.True(response > minimum, $"{id}: response {response:F3} (minimum {minimum})");
    }

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    public void Right_aileron_rolls_right(string id) =>
        AssertResponse(id, u => u with { Aileron = 0.5 }, s => s.Aircraft.State.AngularVelocity.X, 0.3);

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    public void Up_elevator_pitches_up(string id) =>
        AssertResponse(id, u => u with { Elevator = 0.5 }, s => s.Aircraft.State.AngularVelocity.Z, 0.2);

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    public void Right_rudder_yaws_right(string id) =>
        AssertResponse(id, u => u with { Rudder = 0.5 }, s => -s.Aircraft.State.AngularVelocity.Y, 0.1);
}
