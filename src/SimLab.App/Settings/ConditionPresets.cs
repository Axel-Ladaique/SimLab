namespace SimLab.App.Settings;

public enum WindPreset { Calm, Breeze, Windy, Gusty }

public enum TimePreset { Morning, Noon, Evening }

/// <summary>One-click flight conditions for the main menu. A wind preset sets the speed and turbulence and keeps the
/// direction; a time preset sets the sun. Every value sits on the menu sliders' steps.</summary>
public static class ConditionPresets
{
    /// <summary>Slider values are multiples of a decimal step, so they carry float rounding.</summary>
    const double Tolerance = 1e-6;

    static (double Speed, double Turbulence) Wind(WindPreset preset) => preset switch
    {
        WindPreset.Calm => (0, 0),
        WindPreset.Breeze => (3, 0.3),
        WindPreset.Windy => (6, 0.6),
        WindPreset.Gusty => (8, 1.2),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    static (double Azimuth, double Elevation) Sun(TimePreset preset) => preset switch
    {
        TimePreset.Morning => (100, 20),
        TimePreset.Noon => (180, 60),
        TimePreset.Evening => (260, 15),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    public static FlightConditions Apply(FlightConditions conditions, WindPreset preset)
    {
        var (speed, turbulence) = Wind(preset);
        return conditions with { WindSpeed = speed, Turbulence = turbulence };
    }

    public static FlightConditions Apply(FlightConditions conditions, TimePreset preset)
    {
        var (azimuth, elevation) = Sun(preset);
        return conditions with { SunAzimuthDeg = azimuth, SunElevationDeg = elevation };
    }

    /// <summary>The preset these conditions are exactly on, if any.</summary>
    public static WindPreset? MatchWind(FlightConditions conditions)
    {
        foreach (var preset in Enum.GetValues<WindPreset>())
        {
            var (speed, turbulence) = Wind(preset);
            if (Near(conditions.WindSpeed, speed) && Near(conditions.Turbulence, turbulence)) return preset;
        }
        return null;
    }

    public static TimePreset? MatchTime(FlightConditions conditions)
    {
        foreach (var preset in Enum.GetValues<TimePreset>())
        {
            var (azimuth, elevation) = Sun(preset);
            if (Near(conditions.SunAzimuthDeg, azimuth) && Near(conditions.SunElevationDeg, elevation)) return preset;
        }
        return null;
    }

    public static string Key(WindPreset preset) => "WIND_" + preset.ToString().ToUpperInvariant();

    public static string Key(TimePreset preset) => "TIME_" + preset.ToString().ToUpperInvariant();

    static bool Near(double a, double b) => Math.Abs(a - b) < Tolerance;
}
