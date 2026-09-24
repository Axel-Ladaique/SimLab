namespace SimLab.Flight.Geometry;

/// <summary>
/// Body-axis unit vectors (OpenVSP convention): x back (toward the tail), y right (right wing), z up. Forward is −x.
/// Pilot rates in these axes: roll right = −ω_x, pitch up = +ω_y, yaw right = −ω_z.
/// </summary>
public static class BodyAxes
{
    public static readonly Vec3 Forward = new(-1, 0, 0);
    public static readonly Vec3 Right = Vec3.UnitY;
    public static readonly Vec3 Up = Vec3.UnitZ;
}
