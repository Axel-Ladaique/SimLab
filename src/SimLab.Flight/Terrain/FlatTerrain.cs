using SimLab.Flight.Geometry;

namespace SimLab.Flight.Terrain;

public sealed class FlatTerrain : ITerrain
{
    readonly double _elevation;
    readonly CylinderObstacle[] _obstacles;

    public FlatTerrain(double elevation = 0, IEnumerable<CylinderObstacle>? obstacles = null)
    {
        _elevation = elevation;
        _obstacles = obstacles?.ToArray() ?? [];
    }

    public double Height(double x, double y) => _elevation;

    public Vec3 Normal(double x, double y) => Vec3.UnitZ;

    public bool HitsObstacle(Vec3 p)
    {
        foreach (var o in _obstacles)
            if (o.Contains(p)) return true;
        return false;
    }
}
