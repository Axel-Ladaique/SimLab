using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.Flight.Tests.Terrain;

public class ObstacleShapeTests
{
    [Fact]
    public void Planar_yaw_turns_clockwise_seen_from_above()
    {
        var (x, y) = PlanarYaw.ToWorld(1, 0, 90);
        Assert.Equal(0, x, 9);
        Assert.Equal(-1, y, 9);
        var (ex, ey) = PlanarYaw.ToWorld(0, 1, 90);
        Assert.Equal(1, ex, 9);
        Assert.Equal(0, ey, 9);
        var (lx, ly) = PlanarYaw.ToLocal(PlanarYaw.ToWorld(3, -2, 37).X, PlanarYaw.ToWorld(3, -2, 37).Y, 37);
        Assert.Equal(3, lx, 9);
        Assert.Equal(-2, ly, 9);
        Assert.Equal(0, PlanarYaw.Of(1, 0), 9);
        Assert.Equal(90, PlanarYaw.Of(0, -1), 9);
        var (dx, dy) = PlanarYaw.ToWorld(1, 0, PlanarYaw.Of(3, 4));
        Assert.Equal(0.6, dx, 9);
        Assert.Equal(0.8, dy, 9);
    }

    [Fact]
    public void Vertical_cylinder_contains_points_and_catches_a_crossing_segment()
    {
        var pole = new VerticalCylinder(new Vec3(10, 20, 2), 0.05, 8);
        Assert.True(pole.Contains(new Vec3(10.03, 20, 5)));
        Assert.False(pole.Contains(new Vec3(10.1, 20, 5)));
        Assert.False(pole.Contains(new Vec3(10, 20, 10.5)));
        Assert.False(pole.Contains(new Vec3(10, 20, 1.5)));
        // Both ends are outside, the segment passes through the pole.
        Assert.True(pole.Intersects(new Vec3(9, 20, 5), new Vec3(11, 20, 5)));
        Assert.False(pole.Intersects(new Vec3(9, 20.1, 5), new Vec3(11, 20.1, 5)));
        Assert.False(pole.Intersects(new Vec3(9, 20, 11), new Vec3(11, 20, 11)));
        // Diagonal segment entering through the top cap.
        Assert.True(pole.Intersects(new Vec3(10, 20, 12), new Vec3(10.01, 20, 9)));
        Assert.Equal(new Footprint(9.95, 19.95, 10.05, 20.05), pole.Footprint);
    }

    [Fact]
    public void Vertical_cone_narrows_to_its_tip()
    {
        var cone = new VerticalCone(new Vec3(0, 0, 1), 4, 10);
        Assert.True(cone.Contains(new Vec3(3.9, 0, 1)));
        Assert.False(cone.Contains(new Vec3(3.9, 0, 9)));
        Assert.True(cone.Contains(new Vec3(0.3, 0, 10)));
        Assert.False(cone.Contains(new Vec3(0, 0, 11.1)));
        Assert.True(cone.Intersects(new Vec3(-5, 0, 3), new Vec3(5, 0, 3)));
        Assert.False(cone.Intersects(new Vec3(-5, 3.9, 9), new Vec3(5, 3.9, 9)));
    }

    [Fact]
    public void Ellipsoid_uses_its_horizontal_and_vertical_radii()
    {
        var crown = new Ellipsoid(new Vec3(0, 0, 10), 4, 5);
        Assert.True(crown.Contains(new Vec3(3.6, 0, 10)));
        Assert.False(crown.Contains(new Vec3(4.4, 0, 10)));
        Assert.True(crown.Contains(new Vec3(0, 0, 14.5)));
        Assert.False(crown.Contains(new Vec3(0, 0, 15.5)));
        Assert.True(crown.Intersects(new Vec3(-6, 0, 10), new Vec3(6, 0, 10)));
        Assert.False(crown.Intersects(new Vec3(-6, 4.5, 10), new Vec3(6, 4.5, 10)));
        Assert.Equal(new Footprint(-4, -4, 4, 4), crown.Footprint);
    }

    [Fact]
    public void Oriented_box_follows_its_yaw()
    {
        var box = new OrientedBox(new Vec3(100, 50, 2), new Vec3(6, 1, 2), 30);
        Vec3 Local(double x, double y, double z)
        {
            var (wx, wy) = PlanarYaw.ToWorld(x, y, 30);
            return new Vec3(100 + wx, 50 + wy, 2 + z);
        }
        Assert.True(box.Contains(Local(5.9, 0.9, 1.9)));
        Assert.False(box.Contains(Local(6.1, 0, 0)));
        Assert.False(box.Contains(Local(0, 1.1, 0)));
        Assert.False(box.Contains(Local(0, 0, 2.1)));
        Assert.True(box.Intersects(Local(0, -3, 0), Local(0, 3, 0)));
        Assert.False(box.Intersects(Local(-8, 1.2, 0), Local(8, 1.2, 0)));
        var turned = new OrientedBox(Vec3.Zero, new Vec3(6, 1, 2), 90).Footprint;
        Assert.Equal(-1, turned.MinX, 9);
        Assert.Equal(6, turned.MaxY, 9);
    }

    [Fact]
    public void Capsule_catches_a_segment_passing_within_its_radius()
    {
        var wire = new Capsule(new Vec3(20, -10, 5), new Vec3(20, 10, 5), 0.10);
        Assert.True(wire.Contains(new Vec3(20.05, 3, 5)));
        Assert.False(wire.Contains(new Vec3(20.2, 3, 5)));
        Assert.True(wire.Intersects(new Vec3(19, 0, 5.05), new Vec3(21, 0, 5.05)));
        Assert.False(wire.Intersects(new Vec3(19, 0, 5.15), new Vec3(21, 0, 5.15)));
        Assert.False(wire.Intersects(new Vec3(19, 11, 5), new Vec3(21, 11, 5)));
        Assert.Equal(new Footprint(19.9, -10.1, 20.1, 10.1), wire.Footprint);
    }

    [Fact]
    public void Vertical_cylinder_bounds_span_its_base_to_its_top()
    {
        var pole = new VerticalCylinder(new Vec3(10, 20, 2), 0.05, 8);
        Assert.Equal(2, pole.Bounds.MinZ, 9);
        Assert.Equal(10, pole.Bounds.MaxZ, 9);
    }

    [Fact]
    public void Vertical_cone_bounds_span_its_base_to_its_tip()
    {
        var cone = new VerticalCone(new Vec3(0, 0, 1), 4, 10);
        Assert.Equal(1, cone.Bounds.MinZ, 9);
        Assert.Equal(11, cone.Bounds.MaxZ, 9);
    }

    [Fact]
    public void Ellipsoid_bounds_span_its_vertical_radius()
    {
        var crown = new Ellipsoid(new Vec3(0, 0, 10), 4, 5);
        Assert.Equal(5, crown.Bounds.MinZ, 9);
        Assert.Equal(15, crown.Bounds.MaxZ, 9);
    }

    [Fact]
    public void Oriented_box_bounds_cover_its_turned_extent()
    {
        var box = new OrientedBox(new Vec3(100, 50, 2), new Vec3(6, 1, 2), 90);
        Assert.Equal(0, box.Bounds.MinZ, 9);
        Assert.Equal(4, box.Bounds.MaxZ, 9);
    }

    [Fact]
    public void Capsule_bounds_cover_both_ends_plus_radius()
    {
        var wire = new Capsule(new Vec3(20, -10, 5), new Vec3(20, 10, 6), 0.10);
        Assert.Equal(4.9, wire.Bounds.MinZ, 9);
        Assert.Equal(6.1, wire.Bounds.MaxZ, 9);
    }
}
