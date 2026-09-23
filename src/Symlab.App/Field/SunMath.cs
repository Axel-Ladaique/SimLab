using Symlab.Flight.Geometry;

namespace Symlab.App.Field;

public static class SunMath
{
    /// <summary>Unit vector from the ground toward the sun. Azimuth clockwise from north (−Z), elevation above the horizon.</summary>
    public static Vec3 Direction(double azimuthDeg, double elevationDeg)
    {
        double az = Angle.Rad(azimuthDeg), el = Angle.Rad(elevationDeg);
        return new Vec3(Math.Sin(az) * Math.Cos(el), Math.Sin(el), -Math.Cos(az) * Math.Cos(el));
    }
}
