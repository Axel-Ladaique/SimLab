using SimLab.Flight.Geometry;

namespace SimLab.Flight.Terrain;

public interface ITerrain
{
    /// <summary>Ground height (world y) at a horizontal position.</summary>
    double Height(double x, double z);

    /// <summary>Unit ground normal at a horizontal position.</summary>
    Vec3 Normal(double x, double z);

    /// <summary>True if the world point is inside an obstacle (tree, fence, building).</summary>
    bool HitsObstacle(Vec3 p);
}

/// <summary>Vertical cylinder obstacle, e.g. a tree.</summary>
public readonly record struct CylinderObstacle(double X, double Z, double Radius, double Height, double BaseY = 0)
{
    public bool Contains(Vec3 p)
    {
        if (p.Y < BaseY || p.Y > BaseY + Height) return false;
        double dx = p.X - X, dz = p.Z - Z;
        return dx * dx + dz * dz <= Radius * Radius;
    }
}
