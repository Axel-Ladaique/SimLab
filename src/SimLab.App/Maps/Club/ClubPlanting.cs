using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Club;

/// <summary>Deterministic vegetation: the old tree lines, poplars along the road, groves, field trees, bushes and
/// hedges. Distance and height cues for the pilot.</summary>
static class ClubPlanting
{
    static readonly Rgb Oak = new(0.16f, 0.33f, 0.14f);
    static readonly Rgb Fir = new(0.10f, 0.24f, 0.12f);
    static readonly Rgb Poplar = new(0.20f, 0.38f, 0.15f);
    static readonly Rgb Shrub = new(0.14f, 0.30f, 0.12f);

    public static IReadOnlyList<Prop> Plant(int seed)
    {
        var rng = new Random(seed);
        var props = new List<Prop>();

        double Jitter(double amplitude) => (rng.NextDouble() * 2 - 1) * amplitude;
        double Between(double lo, double hi) => lo + rng.NextDouble() * (hi - lo);
        Rgb Tint(Rgb c)
        {
            float k = (float)Between(0.88, 1.12);
            return new Rgb(c.R * k, c.G * k, c.B * k);
        }
        bool Free(double x, double y) => !ClubMap.KeepOut(x, y) && !ClubMap.InCorridor(x, y)
            && Math.Abs(x) < ClubMap.HalfSize - 10 && Math.Abs(y) < ClubMap.HalfSize - 10;
        Vec3 Ground(double x, double y) => new(x, y, ClubMap.Height(x, y));

        // Random values are drawn before the Free check, so rejecting one tree never shifts the others.
        void Broadleaf(double x, double y)
        {
            double h = Between(8, 18), crown = Between(0.28, 0.38) * h, yaw = Between(0, 360);
            var tint = Tint(Oak);
            if (Free(x, y)) props.Add(new BroadleafTree(Ground(x, y), yaw, h, crown, tint));
        }
        void Conifer(double x, double y)
        {
            double h = Between(10, 20), radius = Between(0.22, 0.30) * h, yaw = Between(0, 360);
            var tint = Tint(Fir);
            if (Free(x, y)) props.Add(new ConiferTree(Ground(x, y), yaw, h, radius, tint));
        }

        // Tree lines north, east and west of the field, as before the maps rework.
        for (double x = -400; x <= 400; x += 9) Broadleaf(x + Jitter(2), 140 + Jitter(6));
        for (double y = -200; y <= 130; y += 11)
        {
            Broadleaf(260 + Jitter(4), y + Jitter(3));
            Broadleaf(-260 + Jitter(4), y + Jitter(3));
        }

        // Poplars along the north side of the road (placed on purpose inside its corridor), not on the track.
        for (double x = -ClubMap.HalfSize + 20; x <= ClubMap.HalfSize - 20; x += 12)
        {
            double px = x + Jitter(1), h = Between(18, 25), crown = Between(0.11, 0.14) * h;
            var tint = Tint(Poplar);
            if (Math.Abs(px - ClubMap.TrackX) < 6) continue;
            props.Add(new PoplarTree(Ground(px, ClubMap.RoadY + 8), 0, h, crown, tint));
        }

        // Groves: broadleaf, mixed with conifers far away.
        for (int groves = 0; groves < 30;)
        {
            double cx = Between(-900, 900), cy = Between(-900, 900);
            double distance = Math.Sqrt(cx * cx + cy * cy);
            if (distance < 400) continue;
            groves++;
            int count = rng.Next(25, 71);
            double spread = Between(25, 60);
            for (int k = 0; k < count; k++)
            {
                double a = Between(0, 2 * Math.PI), r = spread * Math.Sqrt(rng.NextDouble());
                double x = cx + r * Math.Cos(a), y = cy + r * Math.Sin(a);
                if (distance > 600 && rng.NextDouble() < 0.6) Conifer(x, y);
                else Broadleaf(x, y);
            }
        }

        // Isolated field trees.
        for (int i = 0; i < 300; i++) Broadleaf(Between(-950, 950), Between(-950, 950));

        // Bushes.
        for (int i = 0; i < 120; i++)
        {
            double x = Between(-950, 950), y = Between(-950, 950), radius = Between(0.8, 1.8), h = Between(1, 2.5), yaw = Between(0, 360);
            var tint = Tint(Shrub);
            if (Free(x, y)) props.Add(new Bush(Ground(x, y), yaw, radius, h, tint));
        }

        props.AddRange(ClubParcels.Hedges(rng));
        return props;
    }
}
