using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Field;

/// <summary>Flat within <see cref="ClubField.FlatRadius"/> of the runway, gentle analytic hills beyond; trees as obstacles.</summary>
public sealed class ClubFieldTerrain : ITerrain
{
    const double NormalStep = 0.5;
    readonly CylinderObstacle[] _trees;
    readonly ObstacleGrid _obstacles;

    public ClubFieldTerrain(IEnumerable<CylinderObstacle> trees)
    {
        _trees = trees.ToArray();
        _obstacles = new ObstacleGrid(_trees.Select(t =>
            new Obstacle(new VerticalCylinder(new Vec3(t.X, t.Y, t.BaseZ), t.Radius, t.Height), ObstacleKind.Tree)));
    }

    public IReadOnlyList<CylinderObstacle> Trees => _trees;

    /// <summary>Ground height at (x east, y north). The hill formula was authored with z = south, hence z = −y.</summary>
    public static double GroundHeight(double x, double y)
    {
        double z = -y;
        double r = Math.Sqrt(x * x + z * z);
        double blend = SmoothStep((r - ClubField.FlatRadius) / ClubField.BlendWidth);
        if (blend <= 0) return 0;
        double h = 0.55 * Math.Sin(x / 137.0 + 0.3) * Math.Cos(z / 191.0 - 1.1)
                 + 0.30 * Math.Sin((x + z) / 83.0 + 2.0)
                 + 0.15 * Math.Cos((x - 2 * z) / 59.0);
        return ClubField.HillAmplitude * blend * (h + 1.0) * 0.5;
    }

    public double Height(double x, double y) => GroundHeight(x, y);

    public Vec3 Normal(double x, double y)
    {
        double dx = (GroundHeight(x + NormalStep, y) - GroundHeight(x - NormalStep, y)) / (2 * NormalStep);
        double dy = (GroundHeight(x, y + NormalStep) - GroundHeight(x, y - NormalStep)) / (2 * NormalStep);
        return new Vec3(-dx, -dy, 1).Normalized();
    }

    public ObstacleKind? HitObstacle(Vec3 p) => _obstacles.Hit(p);

    public ObstacleKind? HitObstacle(Vec3 a, Vec3 b) => _obstacles.Hit(a, b);

    static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
