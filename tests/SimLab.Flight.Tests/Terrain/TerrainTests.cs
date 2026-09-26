using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.Flight.Tests.Terrain;

public class TerrainTests
{
    [Fact]
    public void Flat_terrain_has_constant_height_and_up_normal()
    {
        var t = new FlatTerrain(12.5);
        Assert.Equal(12.5, t.Height(100, -300));
        Assert.Equal(Vec3.UnitZ, t.Normal(0, 0));
    }

    [Fact]
    public void Flat_terrain_reports_the_obstacle_kind()
    {
        var t = new FlatTerrain(0, [new Obstacle(new VerticalCylinder(new Vec3(10, 20, 0), 2, 8), ObstacleKind.Tree)]);
        Assert.Equal(ObstacleKind.Tree, t.HitObstacle(new Vec3(11, 20, 5)));
        Assert.Null(t.HitObstacle(new Vec3(11, 20, 9)));
        Assert.Null(t.HitObstacle(new Vec3(13, 20, 5)));
        Assert.Equal(ObstacleKind.Tree, t.HitObstacle(new Vec3(5, 20, 5), new Vec3(15, 20, 5)));
        Assert.Null(new FlatTerrain().HitObstacle(Vec3.Zero, new Vec3(1, 0, 0)));
    }
}
