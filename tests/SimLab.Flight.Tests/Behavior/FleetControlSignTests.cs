using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>Static sign checks of the fleet's control mixing: each stick channel must produce a moment of the right sign.</summary>
public class FleetControlSignTests
{
    static Vec3 AeroMoment(string id, ControlInputs input)
    {
        var def = Fleet.Load(id);
        var aero = new Aircraft(def).Aero;
        var deflections = def.Controls
            .Select(c => ControlMapping.CommandToDeflection(ControlInputs.Mix(c.Mix, input), c.MaxPositiveDeg, c.MaxNegativeDeg))
            .ToArray();
        var ctx = new AeroContext(new Vec3(-Fleet.Cruise(id).Airspeed, 0, 0), Vec3.Zero, 1.225, 100, BodyAxes.Up, deflections, default);
        return aero.Evaluate(ctx).Moment;
    }

    static Vec3 MomentDueTo(string id, ControlInputs input) => AeroMoment(id, input) - AeroMoment(id, ControlInputs.Neutral);

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    public void Right_aileron_gives_a_right_roll_moment(string id) =>
        Assert.True(MomentDueTo(id, ControlInputs.Neutral with { Aileron = 0.5 }).X < 0, "roll right is -x");

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    public void Up_elevator_gives_a_nose_up_moment(string id) =>
        Assert.True(MomentDueTo(id, ControlInputs.Neutral with { Elevator = 0.5 }).Y > 0);

    /// <summary>
    /// The wing's elevons carry both channels: ±12° for aileron (weight 0.6 of the ±20° throw) and the full ±20° for pitch.
    /// Mix clamps the sum per surface, so full aileron with full elevator saturates the elevon that both push the same way
    /// (−20°) while the other gets −8°: the roll differential shrinks from 24° to 12° but keeps its sign.
    /// </summary>
    [Theory]
    [InlineData(1, 0, -12, 12)]
    [InlineData(0, 1, -20, -20)]
    [InlineData(1, 1, -20, -8)]
    [InlineData(-1, 1, -8, -20)]
    [InlineData(1, -1, 8, 20)]
    public void Wing_elevons_keep_the_full_pitch_throw_with_a_smaller_aileron_throw(double aileron, double elevator, double rightDeg, double leftDeg)
    {
        var controls = Fleet.Load("wing").Controls;
        var input = new ControlInputs(0, aileron, elevator, 0);
        Assert.Equal(rightDeg, Angle.Deg(Aircraft.TargetDeflection(controls.Single(c => c.Name == "elevonRight"), input)), 6);
        Assert.Equal(leftDeg, Angle.Deg(Aircraft.TargetDeflection(controls.Single(c => c.Name == "elevonLeft"), input)), 6);
    }

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("3d")]
    public void Right_rudder_gives_a_nose_right_yaw_moment(string id)
    {
        var m = MomentDueTo(id, ControlInputs.Neutral with { Rudder = 0.5 });
        Assert.True(m.Z < 0, $"{id}: yaw moment {m.Z:F4} N·m (+z is yaw left)");
    }

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("3d")]
    public void Right_rudder_steers_the_rolling_aircraft_to_the_right(string id)
    {
        var def = Fleet.Load(id);
        var ground = new Aircraft(def).Ground;
        var steer = def.Wheels
            .Select(w => ControlInputs.Mix(w.SteerMix, ControlInputs.Neutral with { Rudder = 1 }) * Angle.Rad(w.MaxSteerDeg))
            .ToArray();
        // Pitch the airframe so the steered wheel and the fixed wheels touch together (taildraggers sit tail-down).
        var steered = def.Wheels.First(w => w.SteerMix.Count > 0).Position;
        var fixedWheel = def.Wheels.First(w => w.SteerMix.Count == 0).Position;
        double pitch = Math.Atan((fixedWheel.Z - steered.Z) / (fixedWheel.X - steered.X));
        var q = Attitude.ToOrientation(0, pitch, Math.PI / 2);
        double lowest = def.Wheels.Min(w => q.Rotate(w.Position).Z);
        var rolling = new RigidBodyState(new Vec3(0, 0, -lowest - 0.01), q.Rotate(BodyAxes.Forward * 3), q, Vec3.Zero);
        Assert.Equal(def.Wheels.Count, ground.WheelsInContact(rolling, new FlatTerrain()));
        var m = ground.Evaluate(rolling, new FlatTerrain(), steer).Moment;
        Assert.True(m.Z < 0, $"{id}: yaw moment {m.Z:F4} N·m (+z is yaw left)");
    }
}
