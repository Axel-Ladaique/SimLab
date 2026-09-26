using SimLab.Flight.Geometry;

namespace SimLab.Flight.Terrain;

public interface ITerrain
{
    /// <summary>Ground height (world z, up) at a horizontal position (x east, y north).</summary>
    double Height(double x, double y);

    /// <summary>Unit ground normal (+z up) at a horizontal position (x east, y north).</summary>
    Vec3 Normal(double x, double y);

    /// <summary>Kind of the obstacle (tree, structure, wire) containing the world point, or null.</summary>
    ObstacleKind? HitObstacle(Vec3 p);

    /// <summary>Kind of the first obstacle crossed by the world segment a→b, or null.</summary>
    ObstacleKind? HitObstacle(Vec3 a, Vec3 b);

    /// <summary>Level (world z) of the water surface at (x, y), or null where there is no water.</summary>
    double? WaterSurface(double x, double y);
}
