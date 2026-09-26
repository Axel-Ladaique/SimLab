using SimLab.App.Visual;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Maps;

/// <summary>Deciduous tree (oak): trunk up to 45 % of the height, ellipsoid crown over the top 70 %.</summary>
public sealed record BroadleafTree(Vec3 Base, double YawDeg, double Height, double CrownRadius, Rgb Tint) : Prop(Base, YawDeg)
{
    public double TrunkHeight => 0.45 * Height;
    public double TrunkRadius => Math.Max(0.15, 0.03 * Height);
    public double CrownVerticalRadius => 0.35 * Height;
    public Vec3 CrownCentre => Base + new Vec3(0, 0, Height - CrownVerticalRadius);

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new VerticalCylinder(Base, TrunkRadius, TrunkHeight), ObstacleKind.Tree),
        new(new Ellipsoid(CrownCentre, Foliage.HitScale * CrownRadius, Foliage.HitScale * CrownVerticalRadius), ObstacleKind.Tree),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.Trunk, 0, 0, TrunkHeight / 2, 2 * TrunkRadius, 2 * TrunkRadius, TrunkHeight, Foliage.Bark),
        new(PartMesh.BroadleafCrown, CrownCentre, new Vec3(2 * CrownRadius, 2 * CrownRadius, 2 * CrownVerticalRadius), YawDeg, 0, Tint),
    ];
}

/// <summary>Lombardy poplar: tall and narrow, trunk up to 25 % of the height, crown over the top 85 %.</summary>
public sealed record PoplarTree(Vec3 Base, double YawDeg, double Height, double CrownRadius, Rgb Tint) : Prop(Base, YawDeg)
{
    public double TrunkHeight => 0.25 * Height;
    public double TrunkRadius => Math.Max(0.12, 0.025 * Height);
    public double CrownVerticalRadius => 0.425 * Height;
    public Vec3 CrownCentre => Base + new Vec3(0, 0, Height - CrownVerticalRadius);

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new VerticalCylinder(Base, TrunkRadius, TrunkHeight), ObstacleKind.Tree),
        new(new Ellipsoid(CrownCentre, Foliage.HitScale * CrownRadius, Foliage.HitScale * CrownVerticalRadius), ObstacleKind.Tree),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.Trunk, 0, 0, TrunkHeight / 2, 2 * TrunkRadius, 2 * TrunkRadius, TrunkHeight, Foliage.Bark),
        new(PartMesh.BroadleafCrown, CrownCentre, new Vec3(2 * CrownRadius, 2 * CrownRadius, 2 * CrownVerticalRadius), YawDeg, 0, Tint),
    ];
}

/// <summary>Fir: trunk up to 25 % of the height, conical crown from 15 % to the tip.</summary>
public sealed record ConiferTree(Vec3 Base, double YawDeg, double Height, double BaseRadius, Rgb Tint) : Prop(Base, YawDeg)
{
    public double TrunkHeight => 0.25 * Height;
    public double TrunkRadius => Math.Max(0.12, 0.025 * Height);
    public double CrownBottom => 0.15 * Height;
    public double CrownHeight => Height - CrownBottom;

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new VerticalCylinder(Base, TrunkRadius, TrunkHeight), ObstacleKind.Tree),
        new(new VerticalCone(Base + new Vec3(0, 0, CrownBottom), Foliage.HitScale * BaseRadius, CrownHeight), ObstacleKind.Tree),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.Trunk, 0, 0, TrunkHeight / 2, 2 * TrunkRadius, 2 * TrunkRadius, TrunkHeight, Foliage.Bark),
        Part(PartMesh.ConiferCrown, 0, 0, CrownBottom + CrownHeight / 2, 2 * BaseRadius, 2 * BaseRadius, CrownHeight, Tint),
    ];
}

/// <summary>A round bush standing on the ground.</summary>
public sealed record Bush(Vec3 Base, double YawDeg, double Radius, double Height, Rgb Tint) : Prop(Base, YawDeg)
{
    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new Ellipsoid(Base + new Vec3(0, 0, Height / 2), Foliage.HitScale * Radius, Foliage.HitScale * Height / 2), ObstacleKind.Tree),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.BroadleafCrown, 0, 0, Height / 2, 2 * Radius, 2 * Radius, Height, Tint),
    ];
}

/// <summary>A straight hedge section, its length along the local x axis.</summary>
public sealed record Hedge(Vec3 Base, double YawDeg, double Length, double Width, double Height, Rgb Tint) : Prop(Base, YawDeg)
{
    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new OrientedBox(Base + new Vec3(0, 0, Height / 2),
            new Vec3(Foliage.HitScale * Length / 2, Foliage.HitScale * Width / 2, Foliage.HitScale * Height / 2), YawDeg), ObstacleKind.Tree),
    ];

    public override IEnumerable<PropPart> Parts() => [Part(PartMesh.Box, 0, 0, Height / 2, Length, Width, Height, Tint)];
}
