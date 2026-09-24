using Godot;
using SimLab.App.Mapping;
using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.Game;

public static class GodotConvert
{
    /// <summary>World (ENU: x east, y north, z up) position or direction → Godot world (x east, y up, z south).</summary>
    public static Vector3 WorldToGodot(this Vec3 v) => GodotBasis.WorldToGodot(v).ToVector3();

    /// <summary>Plain component copy, only for vectors already in Godot axes (e.g. from <see cref="GodotBasis.BodyToNodeLocal"/>).</summary>
    public static Vector3 ToVector3(this Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static Quaternion ToGodot(this Quat q) => new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    public static Color ToGodot(this Rgb c, float alpha = 1f) => new(c.R, c.G, c.B, alpha);

    /// <summary>Transform of a node that displays a body at this world (ENU) position and SimLab orientation.</summary>
    public static Transform3D BodyTransform(Vec3 position, Quat orientation) =>
        new(new Basis(GodotBasis.NodeRotation(orientation).ToGodot()), position.WorldToGodot());
}
