namespace SimLab.Flight.Geometry;

/// <summary>
/// Horizontal frames yawed clockwise (seen from above) from the world axes. At 0° local x is east and local y north;
/// at 90° local x is south and local y east. Used for boxes, props and runways.
/// </summary>
public static class PlanarYaw
{
    public static (double X, double Y) ToWorld(double localX, double localY, double yawDeg)
    {
        double a = Angle.Rad(yawDeg), c = Math.Cos(a), s = Math.Sin(a);
        return (localX * c + localY * s, -localX * s + localY * c);
    }

    public static (double X, double Y) ToLocal(double worldX, double worldY, double yawDeg)
    {
        double a = Angle.Rad(yawDeg), c = Math.Cos(a), s = Math.Sin(a);
        return (worldX * c - worldY * s, worldX * s + worldY * c);
    }

    /// <summary>Yaw whose local x axis points along the horizontal direction (dx, dy).</summary>
    public static double Of(double dx, double dy) => Angle.Deg(Math.Atan2(-dy, dx));
}
