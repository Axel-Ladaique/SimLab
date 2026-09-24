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
    public void World_enu_maps_to_godot_east_up_south()
    {
        Near(new Vec3(1, 3, -2), GodotBasis.WorldToGodot(new Vec3(1, 2, 3)));
        Near(new Vec3(0, 0, -1), GodotBasis.WorldToGodot(new Vec3(0, 1, 0)));
        Near(new Vec3(0, 1, 0), GodotBasis.WorldToGodot(Vec3.UnitZ));
    }

    [Fact]
    public void Node_forward_is_body_forward()
        => Near(GodotBasis.WorldToGodot(Sample.Rotate(BodyAxes.Forward)), GodotBasis.NodeRotation(Sample).Rotate(new Vec3(0, 0, -1)));

    [Fact]
    public void Node_right_is_body_right_and_up_is_up()
    {
        var node = GodotBasis.NodeRotation(Sample);
        Near(GodotBasis.WorldToGodot(Sample.Rotate(BodyAxes.Right)), node.Rotate(Vec3.UnitX));
        Near(GodotBasis.WorldToGodot(Sample.Rotate(BodyAxes.Up)), node.Rotate(Vec3.UnitY));
    }

    [Fact]
    public void Level_north_heading_faces_godot_minus_z()
        => Near(new Vec3(0, 0, -1), GodotBasis.NodeRotation(Attitude.ToOrientation(0, 0, 0)).Rotate(new Vec3(0, 0, -1)));

    [Fact]
    public void Body_back_right_up_map_to_node_z_x_y()
        => Near(new Vec3(2, 3, 1), GodotBasis.BodyToNodeLocal(new Vec3(1, 2, 3)));

    [Fact]
    public void Body_points_map_to_the_same_world_points()
    {
        var p = new Vec3(-0.4, 0.75, -0.2);
        Near(GodotBasis.WorldToGodot(Sample.Rotate(p)), GodotBasis.NodeRotation(Sample).Rotate(GodotBasis.BodyToNodeLocal(p)));
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
