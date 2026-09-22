using Symlab.Flight.Aero;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Aero;

public class SurfaceAeroModelTests
{
    static readonly SurfaceSpec Wing = new("wing", SurfaceRole.Wing, Vec3.Zero, 0.75, 0.3, 0.3, 0, 0, 0, 0, "linear", 6, true);
    static readonly SurfaceSpec Stab = new("stab", SurfaceRole.HorizontalTail, new Vec3(-0.8, 0, 0), 0.25, 0.15, 0.15, 0, 0, 0, 0, "linear", 3, true);

    static readonly ControlSurfaceSpec AileronRight = new("aileronRight", "wing", Side.Right, 0.25, 0.4, 1.0, 15, 15, 0.1,
        new Dictionary<string, double> { ["aileron"] = -1 });
    static readonly ControlSurfaceSpec AileronLeft = AileronRight with { Name = "aileronLeft", Side = Side.Left, Mix = new Dictionary<string, double> { ["aileron"] = 1 } };
    static readonly ControlSurfaceSpec Elevator = new("elevator", "stab", Side.Both, 0.4, 0, 1, 20, 20, 0.1,
        new Dictionary<string, double> { ["elevator"] = -1 });
    static readonly ControlSurfaceSpec Flap = new("flap", "wing", Side.Both, 0.25, 0, 1, 20, 20, 0.1,
        new Dictionary<string, double> { ["flap"] = 1 });

    static AeroContext Context(Vec3 air, Vec3 omega = default, double[]? deflections = null, PropWash wash = default) =>
        new(air, omega, 1.225, 100, Vec3.UnitY, deflections ?? [0, 0, 0], wash);

    static Vec3 Flow(double speed, double alphaDeg) =>
        new(speed * Math.Cos(Angle.Rad(alphaDeg)), -speed * Math.Sin(Angle.Rad(alphaDeg)), 0);

    static SurfaceAeroModel WingOnly() => new([Wing], TestAirfoils.Map(), [AileronRight, AileronLeft], []);

    [Fact]
    public void Symmetric_wing_at_positive_alpha_lifts_up_and_drags_back_without_roll_or_yaw()
    {
        var flow = Flow(15, 5);
        var load = WingOnly().Evaluate(Context(flow));
        Assert.True(load.Force.Y > 0);
        Assert.True(-Vec3.Dot(load.Force, flow.Normalized()) > 0, "drag opposes the flow");
        Assert.Equal(0, load.Moment.X, 9);
        Assert.Equal(0, load.Moment.Y, 9);
    }

    [Fact]
    public void Lift_matches_finite_wing_estimate()
    {
        var load = WingOnly().Evaluate(Context(Flow(15, 4)));
        double q = 0.5 * 1.225 * 15 * 15;
        double ar = Wing.AspectRatio;
        double a = 2 * Math.PI / (1 + 2 * Math.PI / (Math.PI * Wing.Oswald * ar));
        double expectedLift = q * Wing.TotalArea * a * Angle.Rad(4);
        double lift = load.Force.Y * Math.Cos(Angle.Rad(4)) + load.Force.X * Math.Sin(Angle.Rad(4));
        Assert.InRange(lift, 0.95 * expectedLift, 1.05 * expectedLift);
    }

    [Fact]
    public void Rolling_right_creates_an_opposing_roll_moment()
    {
        var load = WingOnly().Evaluate(Context(Flow(15, 3), omega: new Vec3(2, 0, 0)));
        Assert.True(load.Moment.X < 0, $"roll damping moment {load.Moment.X}");
    }

    [Fact]
    public void Right_aileron_up_and_left_aileron_down_rolls_right()
    {
        var load = WingOnly().Evaluate(Context(Flow(15, 3), deflections: [-0.2, 0.2, 0]));
        Assert.True(load.Moment.X > 0, $"roll moment {load.Moment.X}");
    }

    [Fact]
    public void Trailing_edge_up_elevator_pitches_nose_up()
    {
        var model = new SurfaceAeroModel([Wing, Stab], TestAirfoils.Map(), [Elevator], []);
        var neutral = model.Evaluate(Context(Flow(15, 2), deflections: [0]));
        var up = model.Evaluate(Context(Flow(15, 2), deflections: [-0.2]));
        Assert.True(up.Moment.Z > neutral.Moment.Z);
    }

    [Fact]
    public void Wing_lift_builds_downwash_at_the_tail()
    {
        var model = new SurfaceAeroModel([Wing, Stab], TestAirfoils.Map(), [Elevator], []);
        for (int i = 0; i < 500; i++)
        {
            model.Evaluate(Context(Flow(15, 5), deflections: [0]));
            model.Advance(0.002);
        }
        Assert.True(model.Downwash > 0.01, $"downwash {model.Downwash}");
        model.Reset();
        Assert.Equal(0, model.Downwash);
    }

    [Fact]
    public void Prop_wash_blows_over_a_stationary_tail()
    {
        var model = new SurfaceAeroModel([Stab], TestAirfoils.Map(), [Elevator], []);
        var wash = new PropWash(new Vec3(0, 0, 0), Vec3.UnitX, 0.5, 10);
        var still = model.Evaluate(Context(Vec3.Zero, deflections: [-0.3]));
        var blown = model.Evaluate(Context(Vec3.Zero, deflections: [-0.3], wash: wash));
        Assert.Equal(Vec3.Zero, still.Force);
        Assert.True(blown.Force.Y < 0, "trailing-edge-up elevator in prop wash pushes the tail down");
    }

    [Fact]
    public void Control_that_covers_no_segment_is_rejected()
    {
        var bad = AileronRight with { SpanStart = 0.99, SpanEnd = 0.995 };
        Assert.Throws<ArgumentException>(() => new SurfaceAeroModel([Wing], TestAirfoils.Map(), [bad], []));
    }

    [Fact]
    public void Overlapping_controls_are_rejected()
        => Assert.Throws<ArgumentException>(() => new SurfaceAeroModel([Wing], TestAirfoils.Map(), [AileronRight, AileronRight with { Name = "flap" }], []));

    [Fact]
    public void Flap_deflection_creates_a_pitching_moment_from_thin_airfoil_theory()
    {
        var model = new SurfaceAeroModel([Wing], TestAirfoils.Map(), [Flap], []);
        var up = model.Evaluate(Context(Flow(15, 0), deflections: [-0.2]));
        var down = model.Evaluate(Context(Flow(15, 0), deflections: [0.2]));
        Assert.True(up.Moment.Z > 0, $"nose-up moment {up.Moment.Z}");
        Assert.True(down.Moment.Z < 0, $"nose-down moment {down.Moment.Z}");
    }

    [Fact]
    public void Fuselage_body_adds_drag()
    {
        var model = new SurfaceAeroModel([], TestAirfoils.Map(), [], [new BodySpec("fuselage", Vec3.Zero, new Vec3(0.01, 0.03, 0.03))]);
        var load = model.Evaluate(Context(new Vec3(20, 0, 0), deflections: []));
        Assert.Equal(-0.5 * 1.225 * 400 * 0.01, load.Force.X, 9);
    }

    static readonly SurfaceSpec SweptWing = Wing with { SweepDeg = 25 };

    [Fact]
    public void Swept_wing_in_sideslip_rolls_away_from_the_wind()
    {
        // Air from the right (beta > 0): the upwind (right) panel sees less effective sweep and lifts more.
        double beta = Angle.Rad(5), alpha = Angle.Rad(4);
        var air = new Vec3(15 * Math.Cos(alpha) * Math.Cos(beta), -15 * Math.Sin(alpha) * Math.Cos(beta), 15 * Math.Sin(beta));
        var swept = new SurfaceAeroModel([SweptWing], TestAirfoils.Map(), [], []).Evaluate(Context(air, deflections: []));
        var straight = new SurfaceAeroModel([Wing], TestAirfoils.Map(), [], []).Evaluate(Context(air, deflections: []));
        Assert.Equal(0, straight.Moment.X, 6);
        Assert.True(swept.Moment.X < -0.05, $"roll moment {swept.Moment.X:F4} N·m");
    }

    [Fact]
    public void Sweep_reduces_the_lift_slope_by_the_cosine_of_the_sweep()
    {
        var flow = Flow(15, 3);
        double straight = new SurfaceAeroModel([Wing], TestAirfoils.Map(), [], []).Evaluate(Context(flow, deflections: [])).Force.Y;
        double swept = new SurfaceAeroModel([SweptWing], TestAirfoils.Map(), [], []).Evaluate(Context(flow, deflections: [])).Force.Y;
        Assert.InRange(swept / straight, 0.88, 0.95);
    }
}
