using SimLab.Flight.Geometry;

namespace SimLab.Flight.Terrain;

public sealed class FlatTerrain : ITerrain
{
    readonly double _elevation;
    readonly ObstacleGrid _obstacles;

    public FlatTerrain(double elevation = 0, IEnumerable<Obstacle>? obstacles = null)
    {
        _elevation = elevation;
        _obstacles = new ObstacleGrid(obstacles ?? []);
    }

    public double Height(double x, double y) => _elevation;

    public Vec3 Normal(double x, double y) => Vec3.UnitZ;

    public ObstacleKind? HitObstacle(Vec3 p) => _obstacles.Hit(p);

    public ObstacleKind? HitObstacle(Vec3 a, Vec3 b) => _obstacles.Hit(a, b);

    public double? WaterSurface(double x, double y) => null;
}
