using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Tests.Behavior;

namespace SimLab.Flight.Tests.Airframe;

public class ControlStepTests
{
    [Fact]
    public void Step_controls_moves_the_servos_to_the_mixed_targets_without_moving_the_body()
    {
        var aircraft = new Aircraft(Fleet.Load("trainer"));
        var start = new RigidBodyState(new Vec3(1, 2, 3), new Vec3(4, 0, 0), Quat.Identity, Vec3.Zero);
        aircraft.Reset(start);
        var input = new ControlInputs(0.5, 0.4, -0.3, 0.6);

        for (int i = 0; i < 100; i++) aircraft.StepControls(0.01, input);

        Assert.Equal(start, aircraft.State);
        var controls = aircraft.Definition.Controls;
        for (int i = 0; i < controls.Count; i++)
            Assert.Equal(Aircraft.TargetDeflection(controls[i], input), aircraft.Deflections[i], 9);
        int rudder = controls.ToList().FindIndex(c => c.Name == "rudder");
        Assert.Equal(Angle.Rad(0.6 * 25), aircraft.Deflections[rudder], 9);
        int nose = aircraft.Definition.Wheels.ToList().FindIndex(w => w.Name == "nose");
        Assert.Equal(Angle.Rad(0.6 * 20), aircraft.SteerAngles[nose], 9);
    }

    [Fact]
    public void Step_controls_is_rate_limited_like_the_servos()
    {
        var aircraft = new Aircraft(Fleet.Load("trainer"));
        int rudder = aircraft.Definition.Controls.ToList().FindIndex(c => c.Name == "rudder");
        aircraft.StepControls(0.01, new ControlInputs(0, 0, 0, 1));
        double maxStep = Angle.Rad(60) / aircraft.Definition.Controls[rudder].ServoSecondsPer60Deg * 0.01;
        Assert.Equal(maxStep, aircraft.Deflections[rudder], 9);
    }
}
