using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Club;

/// <summary>
/// Club buildings, tables, cars, the fence and the power line. Everything stands west of the pilot or behind the
/// pilot line, so nothing collidable is between the pilot and the runway (hand launches fly 3 m in front of the pilot)
/// and the menu's live view, east of the pilot, keeps its framing.
/// </summary>
static class ClubFurniture
{
    static readonly Rgb Wood = new(0.45f, 0.33f, 0.20f);
    static readonly Rgb Cladding = new(0.55f, 0.57f, 0.58f);
    static readonly Rgb Tiles = new(0.35f, 0.20f, 0.16f);
    static readonly Rgb SheetRoof = new(0.30f, 0.30f, 0.32f);
    static readonly Rgb[] Paints = [new(0.70f, 0.10f, 0.10f), new(0.85f, 0.85f, 0.88f), new(0.12f, 0.20f, 0.45f)];

    public static IReadOnlyList<Prop> Place()
    {
        static Vec3 Ground(double x, double y) => new(x, y, ClubMap.Height(x, y));
        var props = new List<Prop>
        {
            new Building(Ground(-52, -38), 0, 12, 6, 3.2, 1.2, false, Cladding, Tiles),
            new Building(Ground(-34, -36), 0, 8, 5, 2.4, 0.15, true, Wood, SheetRoof),
            new Table(Ground(-12, -31), 0, Wood),
            new Table(Ground(-8, -31), 0, Wood),
            new Table(Ground(8, -31), 0, Wood),
            new Table(Ground(12, -31), 0, Wood),
            // Behind the pilot line, between the pilots and the pits; stops short of the windsock at x = 20.
            new Fence(Ground(-3, -27.5), 0, 34, Wood),
        };
        // Parked side by side at the west end of the pits, facing north.
        for (int i = 0; i < Paints.Length; i++) props.Add(new Car(Ground(-64 + 2.7 * i, -44), -90, Paints[i]));
        // Along the south side of the road; the track passes between the poles at x = −75 and −25.
        props.Add(new PowerLine(Enumerable.Range(0, 40).Select(i => Ground(-975 + 50 * i, ClubMap.PowerLineY)).ToArray()));
        return props;
    }
}
