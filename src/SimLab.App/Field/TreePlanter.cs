using SimLab.Flight.Terrain;

namespace SimLab.App.Field;

/// <summary>Deterministic tree lines, hedgerows and scattered trees used as distance and height cues.</summary>
public static class TreePlanter
{
    public static IReadOnlyList<CylinderObstacle> Plant(int seed)
    {
        var rng = new Random(seed);
        var trees = new List<CylinderObstacle>();

        double Jitter(double amplitude) => (rng.NextDouble() * 2 - 1) * amplitude;

        // Positions are generated as (x, z = south) exactly as before the ENU switch, then mapped to y = −z.
        void Add(double x, double z)
        {
            double height = 8 + rng.NextDouble() * 10;
            double y = -z;
            if (Excluded(x, y)) return;
            trees.Add(new CylinderObstacle(x, y, 0.25 * height, height, ClubFieldTerrain.GroundHeight(x, y)));
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
    public static bool Excluded(double x, double y) => Math.Abs(x) < 110 && Math.Abs(y) < 60;
}
