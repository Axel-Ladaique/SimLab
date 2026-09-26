using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Sim;

namespace SimLab.Flight.Tests.Behavior;

public class ControlResponseTests
{
    static void AssertResponse(string id, Func<ControlInputs, ControlInputs> apply, Func<Simulation, double> measure, double minimum,
        double inputSeconds = 0.4, Func<double, double, double>? difference = null)
    {
        var (speed, _) = Fleet.Cruise(id);
        double Run(bool withInput)
        {
            var sim = Fleet.InFlight(id, 80, speed);
            var trim = Fleet.CruiseInputs(id);
            Fleet.Fly(sim, 1.0, _ => trim);
            Fleet.Fly(sim, inputSeconds, _ => withInput ? apply(trim) : trim);
            return measure(sim);
        }
        double withInput = Run(true), baseline = Run(false);
        double response = difference is null ? withInput - baseline : difference(withInput, baseline);
        Assert.True(response > minimum, $"{id}: response {response:F3} (minimum {minimum})");
    }

    static double Pitch(Simulation sim) => Attitude.FromOrientation(sim.Aircraft.State.Orientation).Pitch;

    static double Heading(Simulation sim) => Attitude.FromOrientation(sim.Aircraft.State.Orientation).Heading;

    /// <summary>Signed heading difference a - b wrapped to (-π, π].</summary>
    static double HeadingDifference(double a, double b) => Math.IEEERemainder(a - b, 2 * Math.PI);

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    [InlineData("jet")]
    [InlineData("p51")]
    [InlineData("f18")]
    public void Right_aileron_rolls_right(string id) =>
        AssertResponse(id, u => u with { Aileron = 0.5 }, s => PilotFrame.RollRightRate(s.Aircraft.State.AngularVelocity), 0.3);

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    [InlineData("jet")]
    [InlineData("p51")]
    [InlineData("f18")]
    public void Up_elevator_pitches_up(string id) =>
        // Pitch attitude, not the pitch rate at the end of the input: the rate oscillates with the short period, so a snapshot
        // depends on its phase (the flying wing's well-damped short period is already past its peak at 0.4 s).
        AssertResponse(id, u => u with { Elevator = 0.5 }, Pitch, 5 * Math.PI / 180);

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("3d")]
    [InlineData("jet")]
    [InlineData("p51")]
    [InlineData("f18")]
    public void Right_rudder_yaws_right(string id) =>
        AssertResponse(id, u => u with { Rudder = 0.5 }, Heading, 5 * Math.PI / 180, inputSeconds: 1.0, difference: HeadingDifference);
}
