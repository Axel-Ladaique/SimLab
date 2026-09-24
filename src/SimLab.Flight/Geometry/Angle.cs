namespace SimLab.Flight.Geometry;

public static class Angle
{
    public const double DegToRad = Math.PI / 180.0;
    public static double Rad(double degrees) => degrees * DegToRad;
    public static double Deg(double radians) => radians / DegToRad;
}
