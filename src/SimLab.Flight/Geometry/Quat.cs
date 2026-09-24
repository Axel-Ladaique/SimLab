namespace SimLab.Flight.Geometry;

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

    /// <summary>Rotation taking the orthonormal right-handed basis (a1, a2, a3) onto (b1, b2, b3).</summary>
    /// <exception cref="ArgumentException">The bases differ in handedness, so no rotation maps one onto the other.</exception>
    public static Quat FromBasis(Vec3 a1, Vec3 a2, Vec3 a3, Vec3 b1, Vec3 b2, Vec3 b3)
    {
        if (Vec3.Dot(Vec3.Cross(a1, a2), a3) * Vec3.Dot(Vec3.Cross(b1, b2), b3) <= 0)
            throw new ArgumentException("Bases must have the same handedness (a reflection is not a rotation).");
        // R = Σ b_i a_iᵀ, converted to a quaternion (Shepperd's method).
        double M(int r, int c) => Row(b1, r) * Row(a1, c) + Row(b2, r) * Row(a2, c) + Row(b3, r) * Row(a3, c);
        double m00 = M(0, 0), m11 = M(1, 1), m22 = M(2, 2);
        double trace = m00 + m11 + m22;
        Quat q;
        if (trace > 0)
        {
            double s = 2 * Math.Sqrt(1 + trace);
            q = new Quat((M(2, 1) - M(1, 2)) / s, (M(0, 2) - M(2, 0)) / s, (M(1, 0) - M(0, 1)) / s, s / 4);
        }
        else if (m00 > m11 && m00 > m22)
        {
            double s = 2 * Math.Sqrt(1 + m00 - m11 - m22);
            q = new Quat(s / 4, (M(0, 1) + M(1, 0)) / s, (M(0, 2) + M(2, 0)) / s, (M(2, 1) - M(1, 2)) / s);
        }
        else if (m11 > m22)
        {
            double s = 2 * Math.Sqrt(1 + m11 - m00 - m22);
            q = new Quat((M(0, 1) + M(1, 0)) / s, s / 4, (M(1, 2) + M(2, 1)) / s, (M(0, 2) - M(2, 0)) / s);
        }
        else
        {
            double s = 2 * Math.Sqrt(1 + m22 - m00 - m11);
            q = new Quat((M(0, 2) + M(2, 0)) / s, (M(1, 2) + M(2, 1)) / s, s / 4, (M(1, 0) - M(0, 1)) / s);
        }
        return q.Normalized();
    }

    static double Row(Vec3 v, int i) => i switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}
