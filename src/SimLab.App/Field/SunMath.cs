using SimLab.Flight.Geometry;

namespace SimLab.App.Field;

public static class SunMath
{
    /// <summary>Unit vector from the ground toward the sun. World ENU; azimuth clockwise from north (+y), elevation above the horizon.</summary>
    public static Vec3 Direction(double azimuthDeg, double elevationDeg)
    {
        double az = Angle.Rad(azimuthDeg), el = Angle.Rad(elevationDeg);
        return new Vec3(Math.Sin(az) * Math.Cos(el), Math.Cos(az) * Math.Cos(el), Math.Sin(el));
    }
}
