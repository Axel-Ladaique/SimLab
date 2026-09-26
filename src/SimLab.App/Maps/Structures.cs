using SimLab.App.Visual;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Maps;

/// <summary>A hangar (closed walls and a gable roof) or, when <see cref="Open"/>, a shelter (four posts and a flat
/// roof). Its length runs along local x. Collides as the box around it.</summary>
public sealed record Building(Vec3 Base, double YawDeg, double Length, double Width, double WallHeight, double RoofHeight,
    bool Open, Rgb Walls, Rgb RoofTint) : Prop(Base, YawDeg)
{
    const double PostSize = 0.2;

    public double TotalHeight => WallHeight + RoofHeight;

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new OrientedBox(Base + new Vec3(0, 0, TotalHeight / 2), new Vec3(Length / 2, Width / 2, TotalHeight / 2), YawDeg), ObstacleKind.Structure),
    ];

    public override IEnumerable<PropPart> Parts()
    {
        if (!Open)
            return
            [
                Part(PartMesh.Box, 0, 0, WallHeight / 2, Length, Width, WallHeight, Walls),
                Part(PartMesh.Roof, 0, 0, WallHeight + RoofHeight / 2, Length, Width, RoofHeight, RoofTint),
            ];
        double px = Length / 2 - PostSize, py = Width / 2 - PostSize;
        return
        [
            Part(PartMesh.Post, px, py, WallHeight / 2, PostSize, PostSize, WallHeight, Walls),
            Part(PartMesh.Post, px, -py, WallHeight / 2, PostSize, PostSize, WallHeight, Walls),
            Part(PartMesh.Post, -px, py, WallHeight / 2, PostSize, PostSize, WallHeight, Walls),
            Part(PartMesh.Post, -px, -py, WallHeight / 2, PostSize, PostSize, WallHeight, Walls),
            Part(PartMesh.Box, 0, 0, WallHeight + RoofHeight / 2, Length, Width, RoofHeight, RoofTint),
        ];
    }
}

/// <summary>A parked car, 4.2 × 1.8 × 1.5 m, its length along local x.</summary>
public sealed record Car(Vec3 Base, double YawDeg, Rgb Paint) : Prop(Base, YawDeg)
{
    static readonly Rgb Glass = new(0.15f, 0.18f, 0.20f);

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new OrientedBox(Base + new Vec3(0, 0, 0.75), new Vec3(2.1, 0.9, 0.75), YawDeg), ObstacleKind.Structure),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.Box, 0, 0, 0.55, 4.2, 1.8, 0.8, Paint),
        Part(PartMesh.Box, -0.2, 0, 1.2, 2.2, 1.6, 0.6, Glass),
    ];
}

/// <summary>A 1.8 × 0.8 m trestle table, 0.775 m high.</summary>
public sealed record Table(Vec3 Base, double YawDeg, Rgb Wood) : Prop(Base, YawDeg)
{
    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new OrientedBox(Base + new Vec3(0, 0, 0.3875), new Vec3(0.9, 0.4, 0.3875), YawDeg), ObstacleKind.Structure),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.Box, 0, 0, 0.75, 1.8, 0.8, 0.05, Wood),
        Part(PartMesh.Box, 0.75, 0, 0.36, 0.05, 0.7, 0.72, Wood),
        Part(PartMesh.Box, -0.75, 0, 0.36, 0.05, 0.7, 0.72, Wood),
    ];
}

/// <summary>A 1.1 m post-and-rail fence along local x, a post every 2 m.</summary>
public sealed record Fence(Vec3 Base, double YawDeg, double Length, Rgb Wood) : Prop(Base, YawDeg)
{
    const double Height = 1.1;

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new OrientedBox(Base + new Vec3(0, 0, Height / 2), new Vec3(Length / 2, 0.05, Height / 2), YawDeg), ObstacleKind.Structure),
    ];

    public override IEnumerable<PropPart> Parts()
    {
        int posts = (int)Math.Floor(Length / 2) + 1;
        for (int i = 0; i < posts; i++)
            yield return Part(PartMesh.Post, -Length / 2 + i * Length / (posts - 1), 0, Height / 2, 0.08, 0.08, Height, Wood);
        yield return Part(PartMesh.Box, 0, 0, 1.0, Length, 0.04, 0.06, Wood);
    }
}

/// <summary>
/// Wooden poles 9 m tall carrying two wires, one each side of a cross-arm, that sag 1.5 m mid-span. Each span's wires
/// are drawn and collide as the same 8 straight segments; wires collide with <see cref="WireHitRadius"/>.
/// </summary>
public sealed record PowerLine(IReadOnlyList<Vec3> Poles) : Prop(Poles[0], 0)
{
    public const double PoleHeight = 9;
    public const double PoleRadius = 0.13;
    public const double ArmHalfLength = 0.8;
    public const double AttachHeight = 8.6;
    public const double Sag = 1.5;

    /// <summary>At least half the distance flown in one 500 Hz step at 100 m/s, so a hull segment cannot jump over a wire.</summary>
    public const double WireHitRadius = 0.10;

    const double WireDrawDiameter = 0.03;
    const int SegmentsPerSpan = 8;
    static readonly Rgb Wood = new(0.40f, 0.30f, 0.20f);
    static readonly Rgb Cable = new(0.08f, 0.08f, 0.08f);

    /// <summary>Yaw of span <paramref name="i"/>, from pole i toward pole i + 1 (the last pole uses the last span).</summary>
    double SpanYaw(int i)
    {
        int a = Math.Min(i, Poles.Count - 2);
        return PlanarYaw.Of(Poles[a + 1].X - Poles[a].X, Poles[a + 1].Y - Poles[a].Y);
    }

    /// <summary>Point at <paramref name="t"/> (0…1) along the wire on <paramref name="side"/> (−1 or 1: the span's
    /// local −y or +y) of span <paramref name="span"/>, following the sag.</summary>
    public Vec3 WirePoint(int span, int side, double t)
    {
        var (ox, oy) = PlanarYaw.ToWorld(0, side * ArmHalfLength, SpanYaw(span));
        var offset = new Vec3(ox, oy, AttachHeight);
        var a = Poles[span] + offset;
        var b = Poles[span + 1] + offset;
        return a + (b - a) * t - new Vec3(0, 0, 4 * Sag * t * (1 - t));
    }

    IEnumerable<(Vec3 A, Vec3 B)> WireSegments()
    {
        for (int span = 0; span < Poles.Count - 1; span++)
        foreach (int side in new[] { -1, 1 })
        for (int k = 0; k < SegmentsPerSpan; k++)
            yield return (WirePoint(span, side, k / (double)SegmentsPerSpan), WirePoint(span, side, (k + 1) / (double)SegmentsPerSpan));
    }

    public override IEnumerable<Obstacle> Collision()
    {
        foreach (var p in Poles) yield return new(new VerticalCylinder(p, PoleRadius, PoleHeight), ObstacleKind.Structure);
        foreach (var (a, b) in WireSegments()) yield return new(new Capsule(a, b, WireHitRadius), ObstacleKind.Wire);
    }

    public override IEnumerable<PropPart> Parts()
    {
        for (int i = 0; i < Poles.Count; i++)
        {
            var p = Poles[i];
            yield return new(PartMesh.Post, p + new Vec3(0, 0, PoleHeight / 2), new Vec3(2 * PoleRadius, 2 * PoleRadius, PoleHeight), 0, 0, Wood);
            yield return new(PartMesh.Box, p + new Vec3(0, 0, AttachHeight + 0.1), new Vec3(0.12, 2 * ArmHalfLength + 0.2, 0.12), SpanYaw(i), 0, Wood);
        }
        foreach (var (a, b) in WireSegments())
        {
            var d = b - a;
            double horizontal = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            yield return new(PartMesh.Wire, (a + b) * 0.5, new Vec3(d.Length, WireDrawDiameter, WireDrawDiameter),
                PlanarYaw.Of(d.X, d.Y), Angle.Deg(Math.Atan2(d.Z, horizontal)), Cable);
        }
    }
}
