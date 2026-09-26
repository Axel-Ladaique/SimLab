using SimLab.App.Maps;

namespace SimLab.App.Tests.Maps;

public class WaterBodyTests
{
    static readonly WaterBody Square = new([(-10, -10), (10, -10), (10, 10), (-10, 10)], 3);

    [Fact]
    public void Contains_its_centre_but_not_a_point_outside()
    {
        Assert.True(Square.Contains(0, 0));
        Assert.False(Square.Contains(20, 20));
    }

    // L-shape: a 10×10 square with a 5×5 notch cut from its top-right corner.
    static readonly WaterBody LShape = new(
        [(0, 0), (10, 0), (10, 5), (5, 5), (5, 10), (0, 10)], 1);

    [Fact]
    public void Concave_outline_excludes_its_notch()
    {
        Assert.False(LShape.Contains(7, 7));
        Assert.True(LShape.Contains(2, 2));
    }

    [Fact]
    public void MapTerrain_water_surface_is_the_level_inside_and_null_outside()
    {
        var grid = new HeightGrid(-20, -20, 5, 9, new float[81]);
        var terrain = new MapTerrain(grid, [], [Square]);
        Assert.Equal(3, terrain.WaterSurface(0, 0));
        Assert.Null(terrain.WaterSurface(20, 20));
    }
}
