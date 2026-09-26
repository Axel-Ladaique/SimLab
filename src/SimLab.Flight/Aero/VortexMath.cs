using SimLab.Flight.Geometry;

namespace SimLab.Flight.Aero;

/// <summary>
/// Biot–Savart flow velocities per unit circulation of straight vortex filaments, with a Scully core of radius
/// <c>core</c>: the tangential speed Γ r / (2π (r² + core²)) of an infinite line, so it stays finite on the filament.
/// </summary>
public static class VortexMath
{
    const double FourPi = 4 * Math.PI;

    /// <summary>Velocity at <paramref name="p"/> induced by a unit circulation running from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static Vec3 Segment(Vec3 p, Vec3 a, Vec3 b, double core)
    {
        var r0 = b - a;
        var r1 = p - a;
        var r2 = p - b;
        double n1 = r1.Length, n2 = r2.Length;
        if (n1 < 1e-12 || n2 < 1e-12) return Vec3.Zero;
        var cross = Vec3.Cross(r1, r2);
        double denominator = cross.LengthSquared + core * core * r0.LengthSquared;
        if (denominator < 1e-24) return Vec3.Zero;
        return cross * (Vec3.Dot(r0, r1 / n1 - r2 / n2) / (FourPi * denominator));
    }

    /// <summary>
    /// Velocity at <paramref name="p"/> induced by a unit circulation running from <paramref name="a"/> to infinity along
    /// the unit vector <paramref name="direction"/>.
    /// </summary>
    public static Vec3 SemiInfinite(Vec3 p, Vec3 a, Vec3 direction, double core)
    {
        var r1 = p - a;
        double n1 = r1.Length;
        if (n1 < 1e-12) return Vec3.Zero;
        double along = Vec3.Dot(r1, direction);
        double distanceSquared = Math.Max(0, r1.LengthSquared - along * along) + core * core;
        if (distanceSquared < 1e-24) return Vec3.Zero;
        return Vec3.Cross(direction, r1) * ((1 + along / n1) / (FourPi * distanceSquared));
    }
}
