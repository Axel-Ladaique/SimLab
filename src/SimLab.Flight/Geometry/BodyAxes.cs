namespace SimLab.Flight.Geometry;

/// <summary>Body-axis unit vectors. Current convention: x forward, y up, z right (switched to x back, y right, z up in the body-frame task).</summary>
public static class BodyAxes
{
    public static readonly Vec3 Forward = Vec3.UnitX;
    public static readonly Vec3 Up = Vec3.UnitY;
    public static readonly Vec3 Right = Vec3.UnitZ;
}
