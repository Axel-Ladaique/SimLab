using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Field;

/// <summary>Flat within <see cref="ClubField.FlatRadius"/> of the runway, gentle analytic hills beyond; trees as obstacles.</summary>
public sealed class ClubFieldTerrain : ITerrain
{
    const double NormalStep = 0.5;
    readonly CylinderObstacle[] _trees;

    public ClubFieldTerrain(IEnumerable<CylinderObstacle> trees) => _trees = trees.ToArray();

    public IReadOnlyList<CylinderObstacle> Trees => _trees;

    public static double GroundHeight(double x, double z)
    {
        double r = Math.Sqrt(x * x + z * z);
        double blend = SmoothStep((r - ClubField.FlatRadius) / ClubField.BlendWidth);
        if (blend <= 0) return 0;
        double h = 0.55 * Math.Sin(x / 137.0 + 0.3) * Math.Cos(z / 191.0 - 1.1)
                 + 0.30 * Math.Sin((x + z) / 83.0 + 2.0)
                 + 0.15 * Math.Cos((x - 2 * z) / 59.0);
        return ClubField.HillAmplitude * blend * (h + 1.0) * 0.5;
    }

    public double Height(double x, double z) => GroundHeight(x, z);

    public Vec3 Normal(double x, double z)
    {
        double dx = (GroundHeight(x + NormalStep, z) - GroundHeight(x - NormalStep, z)) / (2 * NormalStep);
        double dz = (GroundHeight(x, z + NormalStep) - GroundHeight(x, z - NormalStep)) / (2 * NormalStep);
        return new Vec3(-dx, 1, -dz).Normalized();
    }

    public bool HitsObstacle(Vec3 p)
    {
        foreach (var t in _trees)
            if (t.Contains(p)) return true;
        return false;
    }

    static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
