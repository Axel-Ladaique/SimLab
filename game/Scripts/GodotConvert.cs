using Godot;
using Symlab.App.Mapping;
using Symlab.App.Visual;
using Symlab.Flight.Geometry;

namespace Symlab.Game;

public static class GodotConvert
{
    public static Vector3 ToGodot(this Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static Quaternion ToGodot(this Quat q) => new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    public static Color ToGodot(this Rgb c, float alpha = 1f) => new(c.R, c.G, c.B, alpha);

    /// <summary>Transform of a node that displays a body at this position and Symlab orientation.</summary>
    public static Transform3D BodyTransform(Vec3 position, Quat orientation) =>
        new(new Basis(GodotBasis.NodeRotation(orientation).ToGodot()), position.ToGodot());
}
