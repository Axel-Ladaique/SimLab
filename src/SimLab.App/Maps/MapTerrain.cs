using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Maps;

/// <summary>A map's ground: a height grid (world x east, y north → z up) and its obstacles.</summary>
public sealed class MapTerrain : ITerrain
{
    public MapTerrain(HeightGrid grid, IEnumerable<Obstacle> obstacles, IReadOnlyList<WaterBody>? water = null)
    {
        Grid = grid;
        Obstacles = new ObstacleGrid(obstacles);
        Water = water ?? [];
    }

    public HeightGrid Grid { get; }
    public ObstacleGrid Obstacles { get; }
    public IReadOnlyList<WaterBody> Water { get; }

    public double Height(double x, double y) => Grid.Height(x, y);

    public Vec3 Normal(double x, double y) => Grid.Normal(x, y);

    public ObstacleKind? HitObstacle(Vec3 p) => Obstacles.Hit(p);

    public ObstacleKind? HitObstacle(Vec3 a, Vec3 b) => Obstacles.Hit(a, b);

    public double? WaterSurface(double x, double y)
    {
        foreach (var body in Water)
            if (body.Contains(x, y)) return body.Level;
        return null;
    }
}
