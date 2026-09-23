using Symlab.Flight.Terrain;

namespace Symlab.App.Field;

/// <summary>Deterministic tree lines, hedgerows and scattered trees used as distance and height cues.</summary>
public static class TreePlanter
{
    public static IReadOnlyList<CylinderObstacle> Plant(int seed)
    {
        var rng = new Random(seed);
        var trees = new List<CylinderObstacle>();

        double Jitter(double amplitude) => (rng.NextDouble() * 2 - 1) * amplitude;

        void Add(double x, double z)
        {
            double height = 8 + rng.NextDouble() * 10;
            if (Excluded(x, z)) return;
            trees.Add(new CylinderObstacle(x, z, 0.25 * height, height, ClubFieldTerrain.GroundHeight(x, z)));
        }

        for (double x = -400; x <= 400; x += 9) Add(x + Jitter(2), -140 + Jitter(6));
        for (double z = -130; z <= 200; z += 11)
        {
            Add(260 + Jitter(4), z + Jitter(3));
            Add(-260 + Jitter(4), z + Jitter(3));
        }
        for (int i = 0; i < 250; i++) Add(rng.NextDouble() * 1800 - 900, rng.NextDouble() * 1800 - 900);
        return trees;
    }

    /// <summary>Keep-out area: runway with safety margins and the pilot box.</summary>
    public static bool Excluded(double x, double z) => Math.Abs(x) < 110 && Math.Abs(z) < 60;
}
