using SimLab.Flight.Geometry;

namespace SimLab.App.Mapping;

/// <summary>
/// SimLab world axes are ENU (x east, y north, z up); Godot world axes are x east, y up, z south, so a world
/// vector maps as (x, y, z) → (x, z, −y). SimLab body axes (<see cref="BodyAxes"/>: x back, y right, z up) map to a
/// Godot node's local axes as back → +Z, right → +X, up → +Y.
/// </summary>
public static class GodotBasis
{
    /// <summary>Rotation taking ENU world vectors to Godot world vectors (−90° about x).</summary>
    static readonly Quat WorldToGodotRotation = Quat.FromAxisAngle(Vec3.UnitX, -Math.PI / 2);

    /// <summary>Rotation taking node-local vectors to body vectors.</summary>
    static readonly Quat NodeToBody = Quat.FromBasis(
        new Vec3(0, 0, -1), Vec3.UnitY, Vec3.UnitX,
        BodyAxes.Forward, BodyAxes.Up, BodyAxes.Right);

    /// <summary>Converts a world (ENU) position or direction to Godot world axes.</summary>
    public static Vec3 WorldToGodot(Vec3 enu) => new(enu.X, enu.Z, -enu.Y);

    /// <summary>Rotation for a Godot node that displays a body with the given SimLab orientation (body → ENU world).</summary>
    public static Quat NodeRotation(Quat bodyOrientation) => (WorldToGodotRotation * bodyOrientation * NodeToBody).Normalized();

    /// <summary>Converts a body-axis vector (x back, y right, z up) to the node's local axes (x right, y up, z back).</summary>
    public static Vec3 BodyToNodeLocal(Vec3 body) => new(body.Y, body.Z, body.X);

    /// <summary>
    /// Rotation (Godot axes) of a prop part: its local x yawed clockwise seen from above by <paramref name="yawDeg"/>
    /// (<see cref="PlanarYaw"/>), then pitched up by <paramref name="pitchDeg"/>. Part meshes are built with local x
    /// along Godot +X, local y along Godot −Z and up along +Y.
    /// </summary>
    public static Quat PartRotation(double yawDeg, double pitchDeg) =>
        (Quat.FromAxisAngle(Vec3.UnitY, -Angle.Rad(yawDeg)) * Quat.FromAxisAngle(Vec3.UnitZ, Angle.Rad(pitchDeg))).Normalized();
}
