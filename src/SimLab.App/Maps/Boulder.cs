using SimLab.App.Visual;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Maps;

/// <summary>A rock: a deformed ellipsoid, a quarter of its height sunk in the ground; collides as the full ellipsoid
/// (solid, and easy to see).</summary>
public sealed record Boulder(Vec3 Base, double YawDeg, double Radius, double Height, Rgb Tint) : Prop(Base, YawDeg)
{
    public Vec3 Centre => Base + new Vec3(0, 0, Height / 2 - 0.25 * Height);

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new Ellipsoid(Centre, Radius, Height / 2), ObstacleKind.Structure),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        new(PartMesh.Rock, Centre, new Vec3(2 * Radius, 2 * Radius, Height), YawDeg, 0, Tint),
    ];
}
