using SimLab.App.Maps;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Tests.Maps;

public class MapModelTests
{
    static readonly MapLayout EastWest = new(new Vec3(0, -25, 0), 1.7, new Vec3(20, -28, 0), Vec3.Zero, 100, 15, 90);

    /// <summary>A runway heading 30°/210°, centred at (500, 200), pilot 20 m to its south-east side.</summary>
    static readonly MapLayout Turned = new(new Vec3(500 + 20 * Math.Cos(Angle.Rad(30)), 200 - 20 * Math.Sin(Angle.Rad(30)), 0),
        1.7, new Vec3(0, 0, 0), new Vec3(500, 200, 0), 200, 20, 30);

    [Theory]
    [InlineData(80, 90)]
    [InlineData(10, 90)]
    [InlineData(260, 270)]
    [InlineData(200, 270)]
    public void Takeoff_is_into_the_wind(double windFrom, double expected) =>
        Assert.Equal(expected, EastWest.TakeoffHeading(windFrom));

    [Fact]
    public void Takeoff_heading_follows_a_turned_runway()
    {
        Assert.Equal(30, Turned.TakeoffHeading(40));
        Assert.Equal(210, Turned.TakeoffHeading(250));
    }

    [Fact]
    public void Takeoff_point_is_inside_the_downwind_threshold()
    {
        var (x, y) = EastWest.TakeoffPoint(90);
        Assert.Equal(-42, x, 9);
        Assert.Equal(0, y, 9);
        Assert.True(EastWest.OnRunway(x, y));
        Assert.True(EastWest.TakeoffPoint(270).X > 0);
        var (tx, ty) = Turned.TakeoffPoint(30);
        Assert.True(Turned.OnRunway(tx, ty));
        // 92 m back from the centre, against the 30° heading.
        Assert.Equal(500 - 92 * Math.Sin(Angle.Rad(30)), tx, 9);
        Assert.Equal(200 - 92 * Math.Cos(Angle.Rad(30)), ty, 9);
    }

    [Fact]
    public void On_runway_uses_the_runway_frame()
    {
        Assert.True(EastWest.OnRunway(49, 7));
        Assert.False(EastWest.OnRunway(51, 0));
        Assert.False(EastWest.OnRunway(0, 8));
        Assert.True(Turned.OnRunway(500 + 99 * Math.Sin(Angle.Rad(30)), 200 + 99 * Math.Cos(Angle.Rad(30))));
        Assert.False(Turned.OnRunway(500 + 101 * Math.Sin(Angle.Rad(30)), 200 + 101 * Math.Cos(Angle.Rad(30))));
    }

    [Fact]
    public void Hand_launch_is_3_m_in_front_of_the_pilot_toward_the_runway()
    {
        var (x, y, heading) = EastWest.HandLaunchPoint(270);
        Assert.Equal(0, x, 9);
        Assert.Equal(-22, y, 9);
        Assert.Equal(270, heading);
        var (tx, ty, _) = Turned.HandLaunchPoint(40);
        double before = Math.Sqrt(Math.Pow(Turned.PilotPosition.X - 500, 2) + Math.Pow(Turned.PilotPosition.Y - 200, 2));
        double after = Math.Sqrt(Math.Pow(tx - 500, 2) + Math.Pow(ty - 200, 2));
        Assert.Equal(before - 3, after, 6);
    }

    [Fact]
    public void Surface_weights_blend_and_stay_normalised()
    {
        var grass = SurfaceWeights.Only(SurfaceKind.Grass);
        var half = grass.Toward(SurfaceKind.Gravel, 0.5);
        Assert.Equal(0.5, half.Grass, 9);
        Assert.Equal(0.5, half.Gravel, 9);
        Assert.Equal(1, half.Toward(SurfaceKind.Wheat, 0.3).Sum, 9);
        Assert.Equal(SurfaceWeights.Only(SurfaceKind.Ploughed), grass.Toward(SurfaceKind.Ploughed, 2));
        Assert.Equal(grass, grass.Toward(SurfaceKind.Dirt, -1));
        foreach (var kind in Enum.GetValues<SurfaceKind>()) Assert.Equal(1, SurfaceWeights.Only(kind).Sum, 9);
    }

    [Fact]
    public void Map_terrain_samples_height_normal_and_obstacles()
    {
        var terrain = new MapTerrain((x, _) => 0.1 * x,
            [new Obstacle(new VerticalCylinder(new Vec3(5, 5, 0.5), 1, 10), ObstacleKind.Structure)]);
        Assert.Equal(2, terrain.Height(20, 7), 9);
        var n = terrain.Normal(3, 4);
        Assert.Equal(1, n.Length, 9);
        Assert.True(n.X < 0 && n.Z > 0.99);
        Assert.Equal(ObstacleKind.Structure, terrain.HitObstacle(new Vec3(5, 5, 3)));
        Assert.Equal(ObstacleKind.Structure, terrain.HitObstacle(new Vec3(0, 5, 3), new Vec3(10, 5, 3)));
        Assert.Null(terrain.HitObstacle(new Vec3(8, 5, 3)));
    }

    [Fact]
    public void Power_line_rejects_fewer_than_two_poles()
    {
        Assert.Throws<ArgumentException>(() => new PowerLine([new Vec3(0, 0, 0)]));
        Assert.Throws<ArgumentException>(() => new PowerLine([]));
        Assert.Equal(2, new PowerLine([new Vec3(0, 0, 0), new Vec3(10, 0, 0)]).Poles.Count);
    }
}
