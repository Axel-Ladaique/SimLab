namespace Symlab.Flight.Geometry;

/// <summary>Unit quaternion (x, y, z, w). As an orientation it maps body vectors to world vectors.</summary>
public readonly record struct Quat(double X, double Y, double Z, double W)
{
    public static readonly Quat Identity = new(0, 0, 0, 1);

    public static Quat FromAxisAngle(Vec3 axis, double angle)
    {
        var n = axis.Normalized();
        var s = Math.Sin(angle / 2);
        return new Quat(n.X * s, n.Y * s, n.Z * s, Math.Cos(angle / 2));
    }

    public static Quat operator *(Quat a, Quat b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public static Quat operator +(Quat a, Quat b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Quat operator *(Quat a, double s) => new(a.X * s, a.Y * s, a.Z * s, a.W * s);

    public Quat Conjugate() => new(-X, -Y, -Z, W);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z + W * W);

    public Quat Normalized()
    {
        var length = Length;
        return length > 1e-12 ? this * (1.0 / length) : Identity;
    }

    public Vec3 Rotate(Vec3 v)
    {
        var u = new Vec3(X, Y, Z);
        var t = 2.0 * Vec3.Cross(u, v);
        return v + W * t + Vec3.Cross(u, t);
    }

    public Vec3 InverseRotate(Vec3 v) => Conjugate().Rotate(v);
}
