using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Maps;

/// <summary>A map's ground: a height function (world x east, y north → z up) and its obstacles.</summary>
public sealed class MapTerrain : ITerrain
{
    const double NormalStep = 0.5;
    readonly Func<double, double, double> _height;

    public MapTerrain(Func<double, double, double> height, IEnumerable<Obstacle> obstacles)
    {
        _height = height;
        Obstacles = new ObstacleGrid(obstacles);
    }

    public ObstacleGrid Obstacles { get; }

    public double Height(double x, double y) => _height(x, y);

    public Vec3 Normal(double x, double y)
    {
        double dx = (_height(x + NormalStep, y) - _height(x - NormalStep, y)) / (2 * NormalStep);
        double dy = (_height(x, y + NormalStep) - _height(x, y - NormalStep)) / (2 * NormalStep);
        return new Vec3(-dx, -dy, 1).Normalized();
    }

    public ObstacleKind? HitObstacle(Vec3 p) => Obstacles.Hit(p);

    public ObstacleKind? HitObstacle(Vec3 a, Vec3 b) => Obstacles.Hit(a, b);
}
