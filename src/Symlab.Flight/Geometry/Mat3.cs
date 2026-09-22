namespace Symlab.Flight.Geometry;

/// <summary>Row-major 3x3 matrix, used for inertia tensors.</summary>
public readonly record struct Mat3(
    double M00, double M01, double M02,
    double M10, double M11, double M12,
    double M20, double M21, double M22)
{
    public static Mat3 Diagonal(double a, double b, double c) => new(a, 0, 0, 0, b, 0, 0, 0, c);

    public static Vec3 operator *(Mat3 m, Vec3 v) => new(
        m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z,
        m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z,
        m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z);

    public double Determinant =>
        M00 * (M11 * M22 - M12 * M21) - M01 * (M10 * M22 - M12 * M20) + M02 * (M10 * M21 - M11 * M20);

    public Mat3 Inverse()
    {
        var det = Determinant;
        if (Math.Abs(det) < 1e-15) throw new InvalidOperationException("Matrix is singular.");
        var k = 1.0 / det;
        return new Mat3(
            (M11 * M22 - M12 * M21) * k, (M02 * M21 - M01 * M22) * k, (M01 * M12 - M02 * M11) * k,
            (M12 * M20 - M10 * M22) * k, (M00 * M22 - M02 * M20) * k, (M02 * M10 - M00 * M12) * k,
            (M10 * M21 - M11 * M20) * k, (M01 * M20 - M00 * M21) * k, (M00 * M11 - M01 * M10) * k);
    }
}
