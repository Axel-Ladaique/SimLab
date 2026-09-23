using Symlab.App.Field;
using Symlab.Flight.Geometry;

namespace Symlab.App.Tests.Field;

public class FieldTests
{
    [Fact]
    public void Terrain_is_flat_around_the_runway_and_hilly_far_away()
    {
        Assert.Equal(0, ClubFieldTerrain.GroundHeight(0, 0));
        Assert.Equal(0, ClubFieldTerrain.GroundHeight(250, -150));
        double maxFar = 0;
        for (double x = -900; x <= 900; x += 50)
            maxFar = Math.Max(maxFar, ClubFieldTerrain.GroundHeight(x, 800));
        Assert.InRange(maxFar, 1, ClubField.HillAmplitude);
    }

    [Fact]
    public void Terrain_height_is_never_negative_and_normals_are_unit_and_upward()
    {
        var terrain = new ClubFieldTerrain([]);
        for (double x = -1000; x <= 1000; x += 97)
        for (double z = -1000; z <= 1000; z += 89)
        {
            Assert.True(terrain.Height(x, z) >= 0);
            var n = terrain.Normal(x, z);
            Assert.Equal(1.0, n.Length, 9);
            Assert.True(n.Y > 0.9);
        }
    }

    [Fact]
    public void Trees_are_deterministic_and_keep_clear_of_the_runway_and_pilot_box()
    {
        var a = TreePlanter.Plant(7);
        var b = TreePlanter.Plant(7);
        Assert.Equal(a, b);
        Assert.True(a.Count > 100, $"only {a.Count} trees");
        Assert.All(a, t =>
        {
            Assert.False(TreePlanter.Excluded(t.X, t.Z));
            Assert.InRange(t.Height, 8, 18);
            Assert.Equal(ClubFieldTerrain.GroundHeight(t.X, t.Z), t.BaseY, 9);
        });
        Assert.True(TreePlanter.Excluded(ClubField.PilotPosition.X, ClubField.PilotPosition.Z));
        Assert.True(TreePlanter.Excluded(45, 0));
    }

    [Fact]
    public void Terrain_reports_tree_hits()
    {
        var tree = TreePlanter.Plant(7)[0];
        var terrain = new ClubFieldTerrain([tree]);
        Assert.True(terrain.HitsObstacle(new Vec3(tree.X, tree.BaseY + 1, tree.Z)));
        Assert.False(terrain.HitsObstacle(new Vec3(0, 5, 0)));
    }

    [Theory]
    [InlineData(80, 90)]
    [InlineData(10, 90)]
    [InlineData(260, 270)]
    [InlineData(200, 270)]
    public void Takeoff_is_into_the_wind(double windFrom, double expectedHeading)
        => Assert.Equal(expectedHeading, ClubField.TakeoffHeading(windFrom));

    [Fact]
    public void Takeoff_point_is_at_the_downwind_end_of_the_runway()
    {
        var (x, z) = ClubField.TakeoffPoint(90);
        Assert.True(x < 0 && ClubField.OnRunway(x, z));
        Assert.True(ClubField.TakeoffPoint(270).X > 0);
    }

    [Fact]
    public void Hand_launch_is_in_front_of_the_pilot()
    {
        var (x, z, heading) = ClubField.HandLaunchPoint(270);
        Assert.Equal(ClubField.PilotPosition.X, x, 9);
        Assert.True(z < ClubField.PilotPosition.Z);
        Assert.Equal(270, heading);
    }

    [Fact]
    public void Sun_direction_follows_azimuth_and_elevation()
    {
        var south = SunMath.Direction(180, 0);
        Assert.Equal(0, south.X, 9);
        Assert.Equal(1, south.Z, 9);
        Assert.Equal(1, SunMath.Direction(0, 90).Y, 9);
        Assert.Equal(1, SunMath.Direction(90, 0).X, 9);
    }

    [Fact]
    public void Windsock_points_downwind_and_hangs_in_calm_air()
    {
        var fromWest = Windsock.Pose(new Vec3(8, 0, 0));
        Assert.Equal(90, fromWest.HeadingDeg, 6);
        Assert.Equal(0, fromWest.DroopDeg, 6);
        Assert.Equal(90, Windsock.Pose(Vec3.Zero).DroopDeg, 6);
        Assert.InRange(Windsock.Pose(new Vec3(0, 0, 3)).DroopDeg, 40, 70);
        Assert.Equal(180, Windsock.Pose(new Vec3(0, 0, 3)).HeadingDeg, 6);
    }
}
