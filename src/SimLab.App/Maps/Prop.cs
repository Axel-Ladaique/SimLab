using SimLab.App.Visual;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Maps;

/// <summary>Unit meshes the game draws prop parts with. Each fills a 1 m cube centred on its origin (local x, local
/// y, up): <see cref="Trunk"/> a tapered upright cylinder, <see cref="Post"/> an upright cylinder, <see cref="Wire"/>
/// a cylinder along local x, <see cref="Box"/> a box, <see cref="Roof"/> a gable prism with its ridge along local x,
/// <see cref="BroadleafCrown"/> a lumpy sphere and <see cref="ConiferCrown"/> a tiered cone (base down).</summary>
public enum PartMesh { Trunk, BroadleafCrown, ConiferCrown, Box, Roof, Post, Wire }

/// <param name="Size">Extent along the part's local x, local y and up (m), before rotation.</param>
/// <param name="YawDeg">See <see cref="PlanarYaw"/>.</param>
/// <param name="PitchDeg">Local x tilted up by this angle after the yaw (wires).</param>
public readonly record struct PropPart(PartMesh Mesh, Vec3 Centre, Vec3 Size, double YawDeg, double PitchDeg, Rgb Tint);

/// <summary>
/// Something standing on a map. Its visible dimensions are defined once; <see cref="Parts"/> (what the game draws)
/// and <see cref="Collision"/> (what the aircraft hits) are both derived from them.
/// </summary>
/// <param name="Base">Ground point the prop stands on (world ENU).</param>
/// <param name="YawDeg">See <see cref="PlanarYaw"/>.</param>
public abstract record Prop(Vec3 Base, double YawDeg)
{
    public abstract IEnumerable<Obstacle> Collision();

    public abstract IEnumerable<PropPart> Parts();

    /// <summary>World point at (x, y) in this prop's yawed frame, z above its base.</summary>
    protected Vec3 At(double localX, double localY, double z)
    {
        var (x, y) = PlanarYaw.ToWorld(localX, localY, YawDeg);
        return new Vec3(Base.X + x, Base.Y + y, Base.Z + z);
    }

    protected PropPart Part(PartMesh mesh, double localX, double localY, double z, double sizeX, double sizeY, double sizeZ, Rgb tint) =>
        new(mesh, At(localX, localY, z), new Vec3(sizeX, sizeY, sizeZ), YawDeg, 0, tint);
}

public static class Foliage
{
    /// <summary>Foliage collides at this fraction of its drawn envelope: brushing the outer leaves survives, the heart
    /// of the crown does not.</summary>
    public const double HitScale = 0.85;

    /// <summary>Foliage meshes have a solid core at this fraction of the envelope, plus lobes or skirts reaching the
    /// envelope, so the hitbox is always inside visible foliage.</summary>
    public const double CoreScale = 0.90;

    public static readonly Rgb Bark = new(0.33f, 0.24f, 0.15f);
}
