using SimLab.Flight.Geometry;

namespace SimLab.Flight.Terrain;

/// <summary>Horizontal bounding rectangle (world x east, y north) of an obstacle, for the spatial grid.</summary>
public readonly record struct Footprint(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>Vertical extent (world z up) of an obstacle, for the grid's cheap reject before Contains/Intersects.</summary>
public readonly record struct VerticalBounds(double MinZ, double MaxZ);

/// <summary>A solid volume the aircraft must not enter. World ENU axes (x east, y north, z up).</summary>
public interface IObstacleShape
{
    bool Contains(Vec3 p);

    /// <summary>True if any point of the segment a→b is inside the shape.</summary>
    bool Intersects(Vec3 a, Vec3 b);

    Footprint Footprint { get; }

    /// <summary>The shape's z extent, so the grid can reject a point or segment well above or below it without
    /// running the shape's full (possibly sampled) test.</summary>
    VerticalBounds Bounds { get; }
}

static class ShapeMath
{
    /// <summary>Spacing of the points tested along a segment by the shapes without an exact segment test. They are
    /// the large ones (crowns, bushes), many times this size.</summary>
    public const double SampleSpacing = 0.10;

    public static bool SampledIntersects(IObstacleShape shape, Vec3 a, Vec3 b)
    {
        var d = b - a;
        int n = Math.Max(1, (int)Math.Ceiling(d.Length / SampleSpacing));
        for (int i = 0; i <= n; i++)
            if (shape.Contains(a + d * (i / (double)n))) return true;
        return false;
    }

    /// <summary>Narrows [t0, t1] to the parameters where start + t·delta lies in [min, max]; false if that is empty.</summary>
    public static bool ClipSlab(double start, double delta, double min, double max, ref double t0, ref double t1)
    {
        if (Math.Abs(delta) < 1e-12) return start >= min && start <= max;
        double ta = (min - start) / delta, tb = (max - start) / delta;
        if (ta > tb) (ta, tb) = (tb, ta);
        t0 = Math.Max(t0, ta);
        t1 = Math.Min(t1, tb);
        return t0 <= t1;
    }

    /// <summary>Squared distance between the segments p1→q1 and p2→q2 (Ericson, Real-Time Collision Detection, 5.1.9).</summary>
    public static double SegmentDistanceSquared(Vec3 p1, Vec3 q1, Vec3 p2, Vec3 q2)
    {
        const double eps = 1e-12;
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        double a = Vec3.Dot(d1, d1), e = Vec3.Dot(d2, d2), f = Vec3.Dot(d2, r);
        double s, t;
        if (a <= eps && e <= eps) return r.LengthSquared;
        if (a <= eps)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            double c = Vec3.Dot(d1, r);
            if (e <= eps)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                double b = Vec3.Dot(d1, d2), denom = a * e - b * b;
                s = denom > eps ? Math.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0, 1);
                }
            }
        }
        return (p1 + d1 * s - (p2 + d2 * t)).LengthSquared;
    }
}

/// <summary>Upright cylinder standing on <see cref="Base"/>: a trunk or a pole. Exact segment test.</summary>
public sealed record VerticalCylinder(Vec3 Base, double Radius, double Height) : IObstacleShape
{
    public bool Contains(Vec3 p)
    {
        if (p.Z < Base.Z || p.Z > Base.Z + Height) return false;
        double dx = p.X - Base.X, dy = p.Y - Base.Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }

    public bool Intersects(Vec3 a, Vec3 b)
    {
        var d = b - a;
        double t0 = 0, t1 = 1;
        if (!ShapeMath.ClipSlab(a.Z, d.Z, Base.Z, Base.Z + Height, ref t0, ref t1)) return false;
        // Closest approach of the horizontal projection to the axis, within the part of the segment at the right height.
        double ax = a.X - Base.X, ay = a.Y - Base.Y, dd = d.X * d.X + d.Y * d.Y;
        double t = dd > 1e-12 ? Math.Clamp(-(ax * d.X + ay * d.Y) / dd, t0, t1) : t0;
        double px = ax + d.X * t, py = ay + d.Y * t;
        return px * px + py * py <= Radius * Radius;
    }

    public Footprint Footprint => new(Base.X - Radius, Base.Y - Radius, Base.X + Radius, Base.Y + Radius);

    public VerticalBounds Bounds => new(Base.Z, Base.Z + Height);
}

/// <summary>Upright cone with its base disc at <see cref="Base"/> and its tip <see cref="Height"/> above: a conifer
/// crown. Sampled segment test.</summary>
public sealed record VerticalCone(Vec3 Base, double BaseRadius, double Height) : IObstacleShape
{
    public bool Contains(Vec3 p)
    {
        double h = p.Z - Base.Z;
        if (h < 0 || h > Height) return false;
        double r = BaseRadius * (1 - h / Height), dx = p.X - Base.X, dy = p.Y - Base.Y;
        return dx * dx + dy * dy <= r * r;
    }

    public bool Intersects(Vec3 a, Vec3 b) => ShapeMath.SampledIntersects(this, a, b);

    public Footprint Footprint => new(Base.X - BaseRadius, Base.Y - BaseRadius, Base.X + BaseRadius, Base.Y + BaseRadius);

    public VerticalBounds Bounds => new(Base.Z, Base.Z + Height);
}

/// <summary>Ellipsoid of revolution about the vertical: a broadleaf crown or a bush. Sampled segment test.</summary>
public sealed record Ellipsoid(Vec3 Centre, double HorizontalRadius, double VerticalRadius) : IObstacleShape
{
    public bool Contains(Vec3 p)
    {
        double dx = p.X - Centre.X, dy = p.Y - Centre.Y, dz = p.Z - Centre.Z;
        return (dx * dx + dy * dy) / (HorizontalRadius * HorizontalRadius) + dz * dz / (VerticalRadius * VerticalRadius) <= 1;
    }

    public bool Intersects(Vec3 a, Vec3 b) => ShapeMath.SampledIntersects(this, a, b);

    public Footprint Footprint => new(Centre.X - HorizontalRadius, Centre.Y - HorizontalRadius,
        Centre.X + HorizontalRadius, Centre.Y + HorizontalRadius);

    public VerticalBounds Bounds => new(Centre.Z - VerticalRadius, Centre.Z + VerticalRadius);
}

/// <summary>Box turned by <see cref="YawDeg"/> (see <see cref="PlanarYaw"/>) about the vertical through its centre;
/// half extents along its local x, local y and up. Exact segment test.</summary>
public sealed record OrientedBox(Vec3 Centre, Vec3 HalfExtents, double YawDeg) : IObstacleShape
{
    public bool Contains(Vec3 p)
    {
        var l = ToLocal(p);
        return Math.Abs(l.X) <= HalfExtents.X && Math.Abs(l.Y) <= HalfExtents.Y && Math.Abs(l.Z) <= HalfExtents.Z;
    }

    public bool Intersects(Vec3 a, Vec3 b)
    {
        var la = ToLocal(a);
        var d = ToLocal(b) - la;
        double t0 = 0, t1 = 1;
        return ShapeMath.ClipSlab(la.X, d.X, -HalfExtents.X, HalfExtents.X, ref t0, ref t1)
            && ShapeMath.ClipSlab(la.Y, d.Y, -HalfExtents.Y, HalfExtents.Y, ref t0, ref t1)
            && ShapeMath.ClipSlab(la.Z, d.Z, -HalfExtents.Z, HalfExtents.Z, ref t0, ref t1);
    }

    public Footprint Footprint
    {
        get
        {
            double a = Angle.Rad(YawDeg), c = Math.Abs(Math.Cos(a)), s = Math.Abs(Math.Sin(a));
            double ex = c * HalfExtents.X + s * HalfExtents.Y, ey = s * HalfExtents.X + c * HalfExtents.Y;
            return new(Centre.X - ex, Centre.Y - ey, Centre.X + ex, Centre.Y + ey);
        }
    }

    public VerticalBounds Bounds => new(Centre.Z - HalfExtents.Z, Centre.Z + HalfExtents.Z);

    Vec3 ToLocal(Vec3 p)
    {
        var (x, y) = PlanarYaw.ToLocal(p.X - Centre.X, p.Y - Centre.Y, YawDeg);
        return new Vec3(x, y, p.Z - Centre.Z);
    }
}

/// <summary>All points within <see cref="Radius"/> of the segment A→B: a wire. Exact segment test.</summary>
public sealed record Capsule(Vec3 A, Vec3 B, double Radius) : IObstacleShape
{
    public bool Contains(Vec3 p) => ShapeMath.SegmentDistanceSquared(p, p, A, B) <= Radius * Radius;

    public bool Intersects(Vec3 a, Vec3 b) => ShapeMath.SegmentDistanceSquared(a, b, A, B) <= Radius * Radius;

    public Footprint Footprint => new(Math.Min(A.X, B.X) - Radius, Math.Min(A.Y, B.Y) - Radius,
        Math.Max(A.X, B.X) + Radius, Math.Max(A.Y, B.Y) + Radius);

    public VerticalBounds Bounds => new(Math.Min(A.Z, B.Z) - Radius, Math.Max(A.Z, B.Z) + Radius);
}
