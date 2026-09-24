using Godot;
using SimLab.App.Mapping;
using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.Game;

public static class GodotConvert
{
    public static Vector3 ToGodot(this Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static Quaternion ToGodot(this Quat q) => new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    public static Color ToGodot(this Rgb c, float alpha = 1f) => new(c.R, c.G, c.B, alpha);

    /// <summary>Transform of a node that displays a body at this position and SimLab orientation.</summary>
    public static Transform3D BodyTransform(Vec3 position, Quat orientation) =>
        new(new Basis(GodotBasis.NodeRotation(orientation).ToGodot()), position.ToGodot());
}
