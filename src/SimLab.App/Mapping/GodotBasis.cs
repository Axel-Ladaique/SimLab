using SimLab.Flight.Geometry;

namespace SimLab.App.Mapping;

/// <summary>
/// SimLab world axes equal Godot world axes (x east, y up, z south). SimLab body axes (x forward, y up, z right)
/// differ from a Godot node's local axes (−Z forward, +Y up, +X right) by a −90° rotation about Y.
/// </summary>
public static class GodotBasis
{
    static readonly Quat NodeToBody = Quat.FromAxisAngle(Vec3.UnitY, -Math.PI / 2);

    /// <summary>Rotation for a Godot node that displays a body with the given SimLab orientation.</summary>
    public static Quat NodeRotation(Quat bodyOrientation) => (bodyOrientation * NodeToBody).Normalized();

    /// <summary>Converts a body-axis vector to the node's local axes.</summary>
    public static Vec3 BodyToNodeLocal(Vec3 body) => new(body.Z, body.Y, -body.X);
}
