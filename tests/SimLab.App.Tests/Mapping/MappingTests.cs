using SimLab.App.Mapping;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Mapping;

public class MappingTests
{
    static void Near(Vec3 e, Vec3 a, int p = 9)
    {
        Assert.Equal(e.X, a.X, p);
        Assert.Equal(e.Y, a.Y, p);
        Assert.Equal(e.Z, a.Z, p);
    }

    static readonly Quat Sample = Attitude.ToOrientation(Angle.Rad(20), Angle.Rad(-10), Angle.Rad(135));

    [Fact]
    public void Node_forward_is_body_forward()
        => Near(Sample.Rotate(Vec3.UnitX), GodotBasis.NodeRotation(Sample).Rotate(new Vec3(0, 0, -1)));

    [Fact]
    public void Node_right_is_body_right_and_up_is_up()
    {
        var node = GodotBasis.NodeRotation(Sample);
        Near(Sample.Rotate(Vec3.UnitZ), node.Rotate(Vec3.UnitX));
        Near(Sample.Rotate(Vec3.UnitY), node.Rotate(Vec3.UnitY));
    }

    [Fact]
    public void Body_points_map_to_the_same_world_points()
    {
        var p = new Vec3(0.4, -0.2, 0.75);
        Near(Sample.Rotate(p), GodotBasis.NodeRotation(Sample).Rotate(GodotBasis.BodyToNodeLocal(p)));
    }

    [Fact]
    public void Nlerp_takes_the_short_way_and_stays_normalized()
    {
        var a = Quat.FromAxisAngle(Vec3.UnitY, 0.1);
        var b = Quat.FromAxisAngle(Vec3.UnitY, 0.3) * -1.0;
        var mid = StateInterpolation.Nlerp(a, b, 0.5);
        Assert.Equal(1.0, mid.Length, 12);
        Near(Quat.FromAxisAngle(Vec3.UnitY, 0.2).Rotate(Vec3.UnitX), mid.Rotate(Vec3.UnitX), 3);
    }

    [Fact]
    public void Interpolate_blends_position_linearly()
    {
        var a = new RigidBodyState(Vec3.Zero, Vec3.Zero, Quat.Identity, Vec3.Zero);
        var b = a with { Position = new Vec3(2, 4, -6) };
        Near(new Vec3(0.5, 1, -1.5), StateInterpolation.Interpolate(a, b, 0.25).Position);
    }
}
