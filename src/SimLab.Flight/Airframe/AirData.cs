using SimLab.Flight.Geometry;

namespace SimLab.Flight.Airframe;

/// <summary>
/// Airspeed (m/s), angle of attack and sideslip (rad; β positive when the air comes from the right), from the air
/// velocity relative to the aircraft in body axes (x back, y right, z up): α = atan2(−u_z, −u_x), β = asin(u_y / V).
/// </summary>
public readonly record struct AirData(double Airspeed, double Alpha, double Beta)
{
    public static AirData From(Vec3 airVelocityBody)
    {
        double v = airVelocityBody.Length;
        if (v < 1e-6) return default;
        var u = airVelocityBody;
        return new AirData(v, Math.Atan2(-u.Z, -u.X), Math.Asin(Math.Clamp(u.Y / v, -1, 1)));
    }
}
