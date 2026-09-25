using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Visual;

public class ReactiveAttitudeTests
{
    public static readonly TheoryData<string> Shipped = new() { "trainer", "sport", "wing", "3d" };

    static readonly double MaxBank = Angle.Rad(ReactiveAttitude.MaxBankDeg);
    static readonly double MaxPitch = Angle.Rad(ReactiveAttitude.MaxPitchDeg);
    static readonly double MaxYaw = Angle.Rad(ReactiveAttitude.MaxYawDeg);

    /// <summary>Servo positions once the servos have reached the commands.</summary>
    static double[] Settled(AircraftDefinition definition, ControlInputs inputs) =>
        definition.Controls.Select(c => Aircraft.TargetDeflection(c, inputs)).ToArray();

    static Reaction Target(AircraftDefinition definition, ControlInputs inputs) =>
        new ReactiveAttitude(definition).Target(Settled(definition, inputs), inputs.Throttle);

    static AircraftDefinition WithMix(string id, string control, string channel, double gain)
    {
        var definition = TestData.Aircraft(id);
        return definition with
        {
            Controls = definition.Controls
                .Select(c => c.Name == control ? c with { Mix = new Dictionary<string, double> { [channel] = gain } } : c)
                .ToList(),
        };
    }

    [Theory, MemberData(nameof(Shipped))]
    public void Right_aileron_banks_right(string id)
    {
        var target = Target(TestData.Aircraft(id), new ControlInputs(0, 0.6, 0, 0));
        Assert.True(target.Bank > 0.3 * MaxBank, $"bank {target.Bank}");
        Assert.True(Math.Abs(target.Pitch) < 0.2 * MaxPitch, $"pitch {target.Pitch}");
    }

    [Theory, MemberData(nameof(Shipped))]
    public void Up_elevator_pitches_the_nose_up(string id)
    {
        var target = Target(TestData.Aircraft(id), new ControlInputs(0, 0, 0.6, 0));
        Assert.True(target.Pitch > 0.3 * MaxPitch, $"pitch {target.Pitch}");
        Assert.True(Math.Abs(target.Bank) < 0.1 * MaxBank, $"bank {target.Bank}");
    }

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    public void Right_rudder_yaws_the_nose_right(string id)
    {
        var target = Target(TestData.Aircraft(id), new ControlInputs(0, 0, 0, 0.6));
        Assert.True(target.Yaw > 0.3 * MaxYaw, $"yaw {target.Yaw}");
    }

    [Fact]
    public void The_wing_has_no_rudder_so_the_rudder_stick_does_nothing()
    {
        var target = Target(TestData.Aircraft("wing"), new ControlInputs(0, 0, 0, 1));
        Assert.Equal(default, target);
    }

    [Theory, MemberData(nameof(Shipped))]
    public void Throttle_moves_the_aircraft_forward_and_idle_brings_it_back(string id)
    {
        var definition = TestData.Aircraft(id);
        var half = Target(definition, new ControlInputs(0.5, 0, 0, 0));
        var full = Target(definition, new ControlInputs(1, 0, 0, 0));
        Assert.True(half.Forward > 0, $"half {half.Forward}");
        Assert.True(full.Forward > half.Forward);
        Assert.Equal(ReactiveAttitude.DefaultMaxForward, full.Forward, 2); // thrust lines are tilted a little
        Assert.Equal(0, Target(definition, ControlInputs.Neutral).Forward);
    }

    [Theory, MemberData(nameof(Shipped))]
    public void Centred_sticks_and_idle_give_the_rest_attitude(string id) =>
        Assert.Equal(default, Target(TestData.Aircraft(id), ControlInputs.Neutral));

    [Theory, MemberData(nameof(Shipped))]
    public void Full_sticks_stay_within_the_limits(string id)
    {
        var definition = TestData.Aircraft(id);
        foreach (var a in new[] { -1.0, 1.0 })
        foreach (var e in new[] { -1.0, 1.0 })
        foreach (var r in new[] { -1.0, 1.0 })
        {
            var t = Target(definition, new ControlInputs(1, a, e, r));
            Assert.InRange(t.Bank, -MaxBank, MaxBank);
            Assert.InRange(t.Pitch, -MaxPitch, MaxPitch);
            Assert.InRange(t.Yaw, -MaxYaw, MaxYaw);
            Assert.InRange(t.Forward, 0, ReactiveAttitude.DefaultMaxForward);
        }
    }

    [Fact]
    public void Full_right_aileron_reaches_the_bank_limit()
    {
        var target = Target(TestData.Aircraft("sport"), new ControlInputs(0, 1, 0, 0));
        Assert.Equal(MaxBank, target.Bank, 6);
    }

    [Fact]
    public void A_reversed_rudder_mix_yaws_the_wrong_way()
    {
        var target = Target(WithMix("trainer", "rudder", "rudder", -1), new ControlInputs(0, 0, 0, 0.6));
        Assert.True(target.Yaw < -0.3 * MaxYaw, $"yaw {target.Yaw}");
    }

    [Fact]
    public void A_reversed_elevator_mix_pitches_the_nose_down()
    {
        var target = Target(WithMix("trainer", "elevator", "elevator", 1), new ControlInputs(0, 0, 0.6, 0));
        Assert.True(target.Pitch < -0.3 * MaxPitch, $"pitch {target.Pitch}");
    }

    [Fact]
    public void A_reversed_radio_channel_banks_the_other_way()
    {
        // A reversed aileron channel turns a right stick into a negative command.
        var target = Target(TestData.Aircraft("sport"), new ControlInputs(0, -0.6, 0, 0));
        Assert.True(target.Bank < -0.3 * MaxBank);
    }

    [Fact]
    public void The_reaction_follows_the_servo_positions_not_the_commands()
    {
        var definition = TestData.Aircraft("sport");
        var reaction = new ReactiveAttitude(definition);
        var centred = new double[definition.Controls.Count];
        Assert.Equal(0, reaction.Target(centred, 0).Bank);
    }

    [Fact]
    public void The_spring_converges_to_the_target_without_overshoot()
    {
        var definition = TestData.Aircraft("trainer");
        var reaction = new ReactiveAttitude(definition);
        var inputs = new ControlInputs(1, 1, 1, 1);
        var deflections = Settled(definition, inputs);
        var target = reaction.Target(deflections, inputs.Throttle);
        double previousBank = 0;
        for (int i = 0; i < 300; i++)
        {
            reaction.Step(1.0 / 60, deflections, inputs.Throttle);
            var c = reaction.Current;
            Assert.True(c.Bank >= previousBank - 1e-12, "bank moves monotonically toward its target from rest");
            Assert.True(c.Bank <= target.Bank + 1e-9);
            Assert.True(c.Forward <= target.Forward + 1e-9);
            previousBank = c.Bank;
        }
        var end = reaction.Current;
        Assert.Equal(target.Bank, end.Bank, 3);
        Assert.Equal(target.Pitch, end.Pitch, 3);
        Assert.Equal(target.Yaw, end.Yaw, 3);
        Assert.Equal(target.Forward, end.Forward, 3);
    }

    [Fact]
    public void The_spring_starts_gently()
    {
        var definition = TestData.Aircraft("trainer");
        var reaction = new ReactiveAttitude(definition);
        var deflections = Settled(definition, new ControlInputs(0, 1, 0, 0));
        reaction.Step(1.0 / 60, deflections, 0);
        Assert.True(reaction.Current.Bank < 0.05 * MaxBank, "no jump on the first frame");
    }

    [Fact]
    public void Reducing_the_throttle_brings_the_aircraft_back_smoothly()
    {
        var definition = TestData.Aircraft("sport");
        var reaction = new ReactiveAttitude(definition);
        var centred = new double[definition.Controls.Count];
        for (int i = 0; i < 300; i++) reaction.Step(1.0 / 60, centred, 1);
        double previous = reaction.Current.Forward;
        for (int i = 0; i < 400; i++)
        {
            reaction.Step(1.0 / 60, centred, 0);
            Assert.True(reaction.Current.Forward <= previous + 1e-12);
            Assert.True(reaction.Current.Forward >= -1e-9, "no overshoot behind the rest position");
            previous = reaction.Current.Forward;
        }
        Assert.Equal(0, reaction.Current.Forward, 3);
    }

    [Fact]
    public void Stick_reversals_and_hitches_never_leave_the_limits()
    {
        var definition = TestData.Aircraft("sport");
        var reaction = new ReactiveAttitude(definition);
        var right = Settled(definition, new ControlInputs(1, 1, 1, 1));
        var left = Settled(definition, new ControlInputs(0, -1, -1, -1));
        var random = new Random(3);
        for (int i = 0; i < 2000; i++)
        {
            bool flip = (i / 7) % 2 == 0;
            double dt = random.NextDouble() < 0.02 ? 5.0 : random.NextDouble() / 30;
            reaction.Step(dt, flip ? right : left, flip ? 1 : 0);
            var c = reaction.Current;
            Assert.InRange(c.Bank, -MaxBank, MaxBank);
            Assert.InRange(c.Pitch, -MaxPitch, MaxPitch);
            Assert.InRange(c.Yaw, -MaxYaw, MaxYaw);
            Assert.InRange(c.Forward, -ReactiveAttitude.DefaultMaxForward, ReactiveAttitude.DefaultMaxForward);
            Assert.False(double.IsNaN(c.Bank + c.Pitch + c.Yaw + c.Forward));
        }
    }

    [Fact]
    public void A_smaller_travel_limit_scales_the_forward_motion()
    {
        var definition = TestData.Aircraft("trainer");
        var reaction = new ReactiveAttitude(definition, maxForward: 0.5);
        Assert.Equal(0.5, reaction.Target(new double[definition.Controls.Count], 1).Forward, 2);
    }

    [Fact]
    public void An_aircraft_without_power_never_moves_forward()
    {
        var definition = TestData.Aircraft("trainer") with { Power = null };
        Assert.Equal(0, new ReactiveAttitude(definition).Target(new double[definition.Controls.Count], 1).Forward);
    }

    [Fact]
    public void The_pose_banks_pitches_yaws_and_moves_the_way_the_reaction_says()
    {
        var origin = new Vec3(1, 2, 30);
        double heading = Angle.Rad(200);
        var rest = ReactiveAttitude.Pose(default, origin, heading);
        Assert.Equal(origin, rest.Position);
        Assert.Equal(200, Angle.Deg(Attitude.FromOrientation(rest.Orientation).Heading), 6);

        var banked = ReactiveAttitude.Pose(new Reaction(0.2, 0, 0, 0), origin, heading);
        Assert.True(banked.Orientation.Rotate(BodyAxes.Right).Z < -0.1, "right bank lowers the right wing");

        var pitched = ReactiveAttitude.Pose(new Reaction(0, 0.1, 0, 0), origin, heading);
        Assert.True(pitched.Orientation.Rotate(BodyAxes.Forward).Z > 0.05, "pitch up raises the nose");

        var yawed = ReactiveAttitude.Pose(new Reaction(0, 0, 0.2, 0), origin, heading);
        Assert.Equal(Angle.Deg(heading + 0.2), Angle.Deg(Attitude.FromOrientation(yawed.Orientation).Heading), 6);

        var ahead = ReactiveAttitude.Pose(new Reaction(0, 0, 0.2, 1.5), origin, heading);
        var forward = rest.Orientation.Rotate(BodyAxes.Forward);
        Assert.Equal(1.5, Vec3.Dot(ahead.Position - origin, forward), 6);
        Assert.Equal(0, ahead.Position.Z - origin.Z, 9);
    }
}
