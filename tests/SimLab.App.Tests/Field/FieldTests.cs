using SimLab.App.Field;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Tests.Field;

public class FieldTests
{
    [Fact]
    public void Terrain_is_flat_around_the_runway_and_hilly_far_away()
    {
        Assert.Equal(0, ClubFieldTerrain.GroundHeight(0, 0));
        Assert.Equal(0, ClubFieldTerrain.GroundHeight(250, 150));
        double maxFar = 0;
        for (double x = -900; x <= 900; x += 50)
            maxFar = Math.Max(maxFar, ClubFieldTerrain.GroundHeight(x, -800));
        Assert.InRange(maxFar, 1, ClubField.HillAmplitude);
    }

    [Fact]
    public void Terrain_height_is_never_negative_and_normals_are_unit_and_upward()
    {
        var terrain = new ClubFieldTerrain([]);
        for (double x = -1000; x <= 1000; x += 97)
        for (double y = -1000; y <= 1000; y += 89)
        {
            Assert.True(terrain.Height(x, y) >= 0);
            var n = terrain.Normal(x, y);
            Assert.Equal(1.0, n.Length, 9);
            Assert.True(n.Z > 0.9);
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
            Assert.False(TreePlanter.Excluded(t.X, t.Y));
            Assert.InRange(t.Height, 8, 18);
            Assert.Equal(ClubFieldTerrain.GroundHeight(t.X, t.Y), t.BaseZ, 9);
        });
        Assert.True(TreePlanter.Excluded(ClubField.PilotPosition.X, ClubField.PilotPosition.Y));
        Assert.True(TreePlanter.Excluded(45, 0));
    }

    [Fact]
    public void Terrain_reports_tree_hits()
    {
        var tree = TreePlanter.Plant(7)[0];
        var terrain = new ClubFieldTerrain([tree]);
        Assert.Equal(ObstacleKind.Tree, terrain.HitObstacle(new Vec3(tree.X, tree.Y, tree.BaseZ + 1)));
        Assert.Null(terrain.HitObstacle(new Vec3(0, 0, 5)));
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
        var (x, y) = ClubField.TakeoffPoint(90);
        Assert.True(x < 0 && ClubField.OnRunway(x, y));
        Assert.True(ClubField.TakeoffPoint(270).X > 0);
    }

    [Fact]
    public void Hand_launch_is_in_front_of_the_pilot()
    {
        var (x, y, heading) = ClubField.HandLaunchPoint(270);
        Assert.Equal(ClubField.PilotPosition.X, x, 9);
        Assert.True(y > ClubField.PilotPosition.Y);
        Assert.Equal(270, heading);
    }

    [Fact]
    public void Sun_direction_follows_azimuth_and_elevation()
    {
        var south = SunMath.Direction(180, 0);
        Assert.Equal(0, south.X, 9);
        Assert.Equal(-1, south.Y, 9);
        Assert.Equal(1, SunMath.Direction(0, 90).Z, 9);
        Assert.Equal(1, SunMath.Direction(90, 0).X, 9);
    }

    [Fact]
    public void Normal_tilts_away_from_the_uphill_direction()
    {
        var terrain = new ClubFieldTerrain([]);

        // Scan x at y = 800 for a point where the ground rises with increasing x, and check the
        // normal there leans the opposite way (X < 0, i.e. away from the uphill slope).
        double xUp = FindRising(x => ClubFieldTerrain.GroundHeight(x, 800));
        Assert.True(terrain.Normal(xUp, 800).X < 0);

        // Same check along y at x = 800: where the ground rises with increasing y, the normal's
        // Y component should be negative.
        double yUp = FindRising(y => ClubFieldTerrain.GroundHeight(800, y));
        Assert.True(terrain.Normal(800, yUp).Y < 0);
    }

    static double FindRising(Func<double, double> heightAt)
    {
        const double step = 10;
        for (double t = -900; t < 900; t += step)
        {
            if (heightAt(t + step) > heightAt(t))
                return t + step / 2;
        }
        throw new InvalidOperationException("No rising segment found in the scanned range.");
    }

    [Fact]
    public void Windsock_points_downwind_and_hangs_in_calm_air()
    {
        var fromWest = Windsock.Pose(new Vec3(8, 0, 0));
        Assert.Equal(90, fromWest.HeadingDeg, 6);
        Assert.Equal(0, fromWest.DroopDeg, 6);
        Assert.Equal(90, Windsock.Pose(Vec3.Zero).DroopDeg, 6);
        Assert.InRange(Windsock.Pose(new Vec3(0, -3, 0)).DroopDeg, 40, 70);
        Assert.Equal(180, Windsock.Pose(new Vec3(0, -3, 0)).HeadingDeg, 6);
    }

    [Fact]
    public void Field_catalog_offers_the_club_field_and_falls_back_to_it()
    {
        Assert.Equal(new[] { "club" }, FieldCatalog.All.Select(f => f.Id));
        Assert.Equal("FIELD_CLUB", FieldCatalog.Find("club").NameKey);
        Assert.Equal("club", FieldCatalog.Find("moon").Id);
    }
}
