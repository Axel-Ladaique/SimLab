using SimLab.Flight.Geometry;

namespace SimLab.Flight.Terrain;

public interface ITerrain
{
    /// <summary>Ground height (world z, up) at a horizontal position (x east, y north).</summary>
    double Height(double x, double y);

    /// <summary>Unit ground normal (+z up) at a horizontal position (x east, y north).</summary>
    Vec3 Normal(double x, double y);

    /// <summary>True if the world point is inside an obstacle (tree, fence, building).</summary>
    bool HitsObstacle(Vec3 p);
}

/// <summary>Vertical cylinder obstacle, e.g. a tree.</summary>
public readonly record struct CylinderObstacle(double X, double Y, double Radius, double Height, double BaseZ = 0)
{
    public bool Contains(Vec3 p)
    {
        if (p.Z < BaseZ || p.Z > BaseZ + Height) return false;
        double dx = p.X - X, dy = p.Y - Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }
}
