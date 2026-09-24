namespace SimLab.Flight.Atmosphere;

/// <param name="SpeedAt10m">Mean wind speed at 10 m above ground, m/s.</param>
/// <param name="FromDirectionDeg">Direction the wind blows from, degrees clockwise from north.</param>
/// <param name="Turbulence">Turbulence scale factor: 0 none, 1 moderate.</param>
/// <param name="RoughnessLength">Surface roughness length for the log wind profile, m.</param>
public sealed record WindSettings(
    double SpeedAt10m = 0,
    double FromDirectionDeg = 0,
    double Turbulence = 0,
    double RoughnessLength = 0.03);
