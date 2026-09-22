using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Flight.Terrain;

namespace Symlab.Flight.Tests.Ground;

public class GroundContactTests
{
    static readonly Dictionary<string, double> NoSteer = new();
    static readonly WheelSpec[] Tricycle =
    [
        new("nose", new Vec3(0.2, -0.1, 0), 1000, 20, 0.03, 0.8, 0, NoSteer),
        new("left", new Vec3(-0.1, -0.1, -0.15), 1000, 20, 0.03, 0.8, 0, NoSteer),
        new("right", new Vec3(-0.1, -0.1, 0.15), 1000, 20, 0.03, 0.8, 0, NoSteer),
    ];
    static readonly MassProperties Cart = MassProperties.FromPrincipal(1.0, 0.05, 0.08, 0.05);
    static readonly CrashLimits Limits = new(MaxGearSinkRate: 3, MaxHullImpactSpeed: 1.5, MaxBellyImpactSpeed: 3);

    static RigidBodyState Simulate(GroundContactModel model, RigidBodyState s, double seconds)
    {
        var terrain = new FlatTerrain();
        WrenchFunction f = (in RigidBodyState st) =>
        {
            var load = model.Evaluate(st, terrain, []);
            return new Wrench(st.Orientation.Rotate(load.Force) + new Vec3(0, -9.81 * Cart.Mass, 0), load.Moment);
        };
        for (int i = 0; i < (int)(seconds / 0.002); i++) s = Rk4Integrator.Step(s, 0.002, Cart, f);
        return s;
    }

    [Fact]
    public void Aircraft_settles_on_its_gear_without_drifting()
    {
        var model = new GroundContactModel(Tricycle, [], Cart.Mass);
        var s = Simulate(model, new RigidBodyState(new Vec3(0, 0.1, 0), Vec3.Zero, Quat.Identity, Vec3.Zero), 5);
        Assert.InRange(s.Position.Y, 0.1 - 9.81 / 3000 - 0.002, 0.1 - 9.81 / 3000 + 0.002);
        Assert.True(s.Velocity.Length < 1e-3, $"velocity {s.Velocity}");
        Assert.True(Math.Abs(s.Position.X) < 1e-3 && Math.Abs(s.Position.Z) < 1e-3);
        Assert.Equal(3, model.WheelsInContact(s, new FlatTerrain()));
    }

    [Fact]
    public void Tires_resist_sideways_sliding()
    {
        var model = new GroundContactModel(Tricycle, [], Cart.Mass);
        var start = new RigidBodyState(new Vec3(0, 0.0967, 0), new Vec3(0, 0, 1), Quat.Identity, Vec3.Zero);
        var s = Simulate(model, start, 2);
        Assert.True(Math.Abs(s.Velocity.Z) < 0.05, $"lateral speed {s.Velocity.Z}");
    }

    [Fact]
    public void Tires_roll_forward_freely()
    {
        var model = new GroundContactModel(Tricycle, [], Cart.Mass);
        var start = new RigidBodyState(new Vec3(0, 0.0967, 0), new Vec3(3, 0, 0), Quat.Identity, Vec3.Zero);
        var s = Simulate(model, start, 1);
        Assert.InRange(s.Velocity.X, 2.5, 3.0);
    }

    [Theory]
    [InlineData("wingtip", CrashCause.WingtipStrike)]
    [InlineData("nose", CrashCause.NoseOver)]
    [InlineData("tail", CrashCause.HullImpact)]
    public void Fast_hull_impact_is_a_crash(string tag, CrashCause expected)
    {
        var model = new GroundContactModel([], [new HullPointSpec("p", Vec3.Zero, tag)], 1.0);
        var s = new RigidBodyState(new Vec3(0, -0.001, 0), new Vec3(0, -2, 0), Quat.Identity, Vec3.Zero);
        Assert.Equal(expected, model.DetectCrash(s, new FlatTerrain(), Limits));
    }

    [Fact]
    public void Gentle_belly_landing_is_not_a_crash()
    {
        var model = new GroundContactModel([], [new HullPointSpec("belly", Vec3.Zero, "belly")], 1.0);
        var s = new RigidBodyState(new Vec3(0, -0.001, 0), new Vec3(8, -2, 0), Quat.Identity, Vec3.Zero);
        Assert.Equal(CrashCause.None, model.DetectCrash(s, new FlatTerrain(), Limits));
    }

    [Fact]
    public void Hard_gear_landing_is_a_crash()
    {
        var model = new GroundContactModel(Tricycle, [], 1.0);
        var s = new RigidBodyState(new Vec3(0, 0.099, 0), new Vec3(0, -4, 0), Quat.Identity, Vec3.Zero);
        Assert.Equal(CrashCause.HardLanding, model.DetectCrash(s, new FlatTerrain(), Limits));
    }

    [Fact]
    public void Flying_into_a_tree_is_a_crash()
    {
        var model = new GroundContactModel([], [new HullPointSpec("nose", new Vec3(0.3, 0, 0), "nose")], 1.0);
        var terrain = new FlatTerrain(0, [new CylinderObstacle(10.3, 0, 1, 10)]);
        var s = new RigidBodyState(new Vec3(10, 5, 0), new Vec3(10, 0, 0), Quat.Identity, Vec3.Zero);
        Assert.Equal(CrashCause.TreeStrike, model.DetectCrash(s, terrain, Limits));
    }
}
