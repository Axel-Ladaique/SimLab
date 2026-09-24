using SimLab.Flight.Atmosphere;

namespace SimLab.App.Settings;

/// <param name="WindSpeed">Mean wind at 10 m, m/s.</param>
/// <param name="WindFromDeg">Direction the wind blows from, clockwise from north.</param>
/// <param name="Turbulence">Dryden intensity scale (0 none, 1 moderate).</param>
/// <param name="SunAzimuthDeg">Sun azimuth, clockwise from north.</param>
/// <param name="SunElevationDeg">Sun elevation above the horizon.</param>
public sealed record FlightConditions(
    double WindSpeed = 0,
    double WindFromDeg = 270,
    double Turbulence = 0,
    double SunAzimuthDeg = 200,
    double SunElevationDeg = 40,
    int Seed = 1)
{
    public WindSettings ToWindSettings() => new(WindSpeed, WindFromDeg, Turbulence);
}
