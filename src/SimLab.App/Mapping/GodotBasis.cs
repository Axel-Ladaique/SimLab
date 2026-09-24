using SimLab.Flight.Geometry;

namespace SimLab.App.Mapping;

/// <summary>
/// SimLab world axes are ENU (x east, y north, z up); Godot world axes are x east, y up, z south, so a world
/// vector maps as (x, y, z) → (x, z, −y). SimLab body axes (<see cref="BodyAxes"/>) map to a Godot node's
/// local axes as forward → −Z, up → +Y, right → +X.
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

    /// <summary>Converts a body-axis vector to the node's local axes.</summary>
    public static Vec3 BodyToNodeLocal(Vec3 body) =>
        new(Vec3.Dot(body, BodyAxes.Right), Vec3.Dot(body, BodyAxes.Up), -Vec3.Dot(body, BodyAxes.Forward));
}
