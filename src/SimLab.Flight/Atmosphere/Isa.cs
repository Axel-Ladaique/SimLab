namespace SimLab.Flight.Atmosphere;

/// <summary>International Standard Atmosphere, troposphere only.</summary>
public static class Isa
{
    public const double SeaLevelDensity = 1.225;
    public const double DynamicViscosity = 1.81e-5;
    const double SeaLevelPressure = 101325;
    const double SeaLevelTemperature = 288.15;
    const double LapseRate = 0.0065;
    const double GasConstant = 287.05;

    public static double Density(double altitudeM, double temperatureOffsetK = 0)
    {
        double pressure = SeaLevelPressure * Math.Pow(1 - LapseRate * altitudeM / SeaLevelTemperature, 5.2559);
        double temperature = SeaLevelTemperature + temperatureOffsetK - LapseRate * altitudeM;
        return pressure / (GasConstant * temperature);
    }
}
