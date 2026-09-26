using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Club;

/// <summary>Farmland beyond the club: a grid of 140 m parcels turned 12°, each wheat, ploughed or meadow, some edged
/// with hedges.</summary>
static class ClubParcels
{
    const double Size = 140;
    const double YawDeg = 12;
    const double HedgeSection = 20;
    const int SectionsPerEdge = 6;               // 120 m of hedge, 10 m gaps at the corners
    static readonly Rgb Leaves = new(0.14f, 0.30f, 0.12f);

    public static SurfaceKind KindAt(double x, double y)
    {
        var (lx, ly) = PlanarYaw.ToLocal(x, y, YawDeg);
        return (Hash((int)Math.Floor(lx / Size), (int)Math.Floor(ly / Size)) % 3) switch
        {
            0 => SurfaceKind.Wheat,
            1 => SurfaceKind.Ploughed,
            _ => SurfaceKind.Grass,
        };
    }

    /// <summary>Hedges on the north and east edges of the parcels whose hash picks them, in 20 m sections so they
    /// follow the relief; sections that would reach the club, a corridor or the map edge are left out.</summary>
    public static IEnumerable<Hedge> Hedges(Random rng)
    {
        for (int i = -11; i <= 10; i++)
        for (int j = -11; j <= 10; j++)
        for (int edge = 0; edge < 2; edge++)
        {
            if (((Hash(i, j) >> (edge + 2)) & 1) == 0) continue;
            // Edge centre and direction in the parcel frame: north edges run along local x, east edges along local y.
            double cx = edge == 0 ? (i + 0.5) * Size : (i + 1) * Size;
            double cy = edge == 0 ? (j + 1) * Size : (j + 0.5) * Size;
            double hedgeYaw = edge == 0 ? YawDeg : YawDeg - 90;
            for (int s = 0; s < SectionsPerEdge; s++)
            {
                double along = (s - (SectionsPerEdge - 1) / 2.0) * HedgeSection;
                var (lx, ly) = edge == 0 ? (cx + along, cy) : (cx, cy + along);
                var (x, y) = PlanarYaw.ToWorld(lx, ly, YawDeg);
                if (!Allowed(x, y, hedgeYaw)) continue;
                float k = (float)(0.88 + rng.NextDouble() * 0.24);
                yield return new Hedge(new Vec3(x, y, ClubMap.Height(x, y)), hedgeYaw, HedgeSection, 1.5, 2.2,
                    new Rgb(Leaves.R * k, Leaves.G * k, Leaves.B * k));
            }
        }
    }

    static bool Allowed(double x, double y, double yawDeg)
    {
        for (int e = -1; e <= 1; e++)
        {
            var (dx, dy) = PlanarYaw.ToWorld(e * HedgeSection / 2, 0, yawDeg);
            double px = x + dx, py = y + dy;
            if (Math.Abs(px) > ClubMap.HalfSize - 20 || Math.Abs(py) > ClubMap.HalfSize - 20) return false;
            if (Math.Sqrt(px * px + py * py) < ClubMap.FarmlandRadius + 30) return false;
            if (ClubMap.InCorridor(px, py) || ClubMap.KeepOut(px, py)) return false;
        }
        return true;
    }

    static int Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 73856093u ^ (uint)b * 19349663u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }
}
