using Symlab.Flight.Geometry;

namespace Symlab.Flight.Airframe;

/// <summary>Airspeed (m/s), angle of attack and sideslip (rad; β positive when the air comes from the right).</summary>
public readonly record struct AirData(double Airspeed, double Alpha, double Beta)
{
    public static AirData From(Vec3 airVelocityBody)
    {
        double v = airVelocityBody.Length;
        if (v < 1e-6) return default;
        return new AirData(v, Math.Atan2(-airVelocityBody.Y, airVelocityBody.X), Math.Asin(Math.Clamp(airVelocityBody.Z / v, -1, 1)));
    }
}
