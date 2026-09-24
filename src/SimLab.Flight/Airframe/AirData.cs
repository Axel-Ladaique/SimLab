using SimLab.Flight.Geometry;

namespace SimLab.Flight.Airframe;

/// <summary>Airspeed (m/s), angle of attack and sideslip (rad; β positive when the air comes from the right).</summary>
public readonly record struct AirData(double Airspeed, double Alpha, double Beta)
{
    public static AirData From(Vec3 airVelocityBody)
    {
        double v = airVelocityBody.Length;
        if (v < 1e-6) return default;
        double forward = Vec3.Dot(airVelocityBody, BodyAxes.Forward);
        double up = Vec3.Dot(airVelocityBody, BodyAxes.Up);
        double right = Vec3.Dot(airVelocityBody, BodyAxes.Right);
        return new AirData(v, Math.Atan2(-up, forward), Math.Asin(Math.Clamp(right / v, -1, 1)));
    }
}
