using Symlab.Flight.Geometry;
using Symlab.Flight.Terrain;

namespace Symlab.Flight.Tests.Terrain;

public class TerrainTests
{
    [Fact]
    public void Flat_terrain_has_constant_height_and_up_normal()
    {
        var t = new FlatTerrain(12.5);
        Assert.Equal(12.5, t.Height(100, -300));
        Assert.Equal(Vec3.UnitY, t.Normal(0, 0));
    }

    [Fact]
    public void Point_inside_tree_cylinder_hits_obstacle()
    {
        var t = new FlatTerrain(0, [new CylinderObstacle(10, 20, 2, 8)]);
        Assert.True(t.HitsObstacle(new Vec3(11, 5, 20)));
        Assert.False(t.HitsObstacle(new Vec3(11, 9, 20)));
        Assert.False(t.HitsObstacle(new Vec3(13, 5, 20)));
    }
}
