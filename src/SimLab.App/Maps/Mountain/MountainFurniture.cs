using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Mountain;

/// <summary>
/// The mountain's buildings and works: two chalets and a hut east of the strip with a small car park at the road's
/// end, the chairlift from the lake's east shore up to the knob north of the strip, and the road bridge over the
/// stream.
/// </summary>
public static class MountainFurniture
{
    static readonly Rgb DarkWood = new(0.36f, 0.25f, 0.16f);
    static readonly Rgb Slate = new(0.30f, 0.30f, 0.32f);
    static readonly Rgb Concrete = new(0.55f, 0.55f, 0.53f);
    static readonly Rgb Stone = new(0.46f, 0.45f, 0.42f);
    static readonly Rgb[] Paints = [new(0.12f, 0.20f, 0.45f), new(0.80f, 0.80f, 0.82f)];

    /// <summary>Pylons stand about this far apart, closer where the ground bulges up under a span.</summary>
    const double PylonSpacing = 150;

    /// <summary>Every cable stays at least this high above the ground.</summary>
    const double CableClearance = 5;

    public static IEnumerable<Prop> Place(HeightGrid grid, MountainRoad road)
    {
        Vec3 Ground(double x, double y) => new(x, y, grid.Height(x, y));
        var props = new List<Prop>
        {
            Chalet(grid, 242, -52, 0, 12, 9, 5),
            Chalet(grid, 276, -94, 20, 12, 9, 5),
            Chalet(grid, 226, -114, -10, 6, 5, 3),
        };
        // The car park at the road's end.
        for (int i = 0; i < Paints.Length; i++) props.Add(new Car(Ground(207 + 3 * i, -62 - 1.5 * i), 60, Paints[i]));

        // The chairlift, its stations just beyond its end pylons.
        var lift = Lift(grid);
        props.Add(lift);
        double yaw = PlanarYaw.Of(MountainMap.LiftTop.X - MountainMap.LiftBottom.X, MountainMap.LiftTop.Y - MountainMap.LiftBottom.Y);
        var (ux, uy) = PlanarYaw.ToWorld(1, 0, yaw);
        props.Add(Station(grid, MountainMap.LiftBottom.X - 7 * ux, MountainMap.LiftBottom.Y - 7 * uy, yaw));
        props.Add(Station(grid, MountainMap.LiftTop.X + 7 * ux, MountainMap.LiftTop.Y + 7 * uy, yaw));

        var crossing = road.StreamCrossing;
        props.Add(new Bridge(new Vec3(crossing.X, crossing.Y, crossing.Profile), crossing.YawDeg,
            MountainRelief.BridgeLength, MountainRelief.BridgeWidth, Stone));
        return props;
    }

    /// <summary>A dark-wood chalet with a 45° slate roof, its floor at its lowest corner so no wall floats.</summary>
    static Building Chalet(HeightGrid grid, double x, double y, double yaw, double length, double width, double walls) =>
        new(OnLowestCorner(grid, x, y, yaw, length, width), yaw, length, width, walls, width / 2, false, DarkWood, Slate, SteepRoof: true);

    static Building Station(HeightGrid grid, double x, double y, double yaw) =>
        new(OnLowestCorner(grid, x, y, yaw, 8, 5), yaw, 8, 5, 3, 1, false, Concrete, Slate);

    static Vec3 OnLowestCorner(HeightGrid grid, double x, double y, double yaw, double length, double width)
    {
        double z = double.PositiveInfinity;
        foreach (var (cx, cy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            var (wx, wy) = PlanarYaw.ToWorld(cx * length / 2, cy * width / 2, yaw);
            z = Math.Min(z, grid.Height(x + wx, y + wy));
        }
        return new Vec3(x, y, z);
    }

    /// <summary>Pylons about <see cref="PylonSpacing"/> apart along the lift's line; wherever a span's cables would
    /// pass lower than <see cref="CableClearance"/> over the ground (the brow of the face, a cliff band), a pylon is
    /// added at the lowest point.</summary>
    static Cableway Lift(HeightGrid grid)
    {
        var (a, b) = (MountainMap.LiftBottom, MountainMap.LiftTop);
        double length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        int spans = (int)Math.Round(length / PylonSpacing);
        var at = Enumerable.Range(0, spans + 1).Select(k => k / (double)spans).ToList();
        Vec3 Pylon(double t) { double x = a.X + t * (b.X - a.X), y = a.Y + t * (b.Y - a.Y); return new(x, y, grid.Height(x, y)); }

        for (int guard = 0; guard < 40; guard++)
        {
            var lift = new Cableway(at.Select(Pylon).ToArray());
            (double Clearance, double T) worst = (double.PositiveInfinity, 0);
            for (int span = 0; span + 1 < at.Count; span++)
            for (double s = 0.02; s < 1; s += 0.02)
            foreach (int side in new[] { -1, 1 })
            {
                var c = lift.CablePoint(span, side, s);
                double clearance = c.Z - grid.Height(c.X, c.Y);
                if (clearance < worst.Clearance) worst = (clearance, at[span] + s * (at[span + 1] - at[span]));
            }
            if (worst.Clearance >= CableClearance) return lift;
            at.Add(worst.T);
            at.Sort();
        }
        throw new InvalidOperationException("The chairlift's cables cannot clear the ground.");
    }
}
