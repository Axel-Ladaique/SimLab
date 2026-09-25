using SimLab.App.Settings;

namespace SimLab.App.Tests.Settings;

public class ConditionPresetsTests
{
    [Theory]
    [InlineData(WindPreset.Calm, 0, 0)]
    [InlineData(WindPreset.Breeze, 3, 0.3)]
    [InlineData(WindPreset.Windy, 6, 0.6)]
    [InlineData(WindPreset.Gusty, 8, 1.2)]
    public void Wind_preset_sets_speed_and_turbulence_and_keeps_the_rest(WindPreset preset, double speed, double turbulence)
    {
        var before = new FlightConditions(WindSpeed: 1, WindFromDeg: 135, Turbulence: 0.9, SunAzimuthDeg: 90, SunElevationDeg: 30, Seed: 4);
        var after = ConditionPresets.Apply(before, preset);
        Assert.Equal(before with { WindSpeed = speed, Turbulence = turbulence }, after);
        Assert.Equal(preset, ConditionPresets.MatchWind(after));
    }

    [Theory]
    [InlineData(TimePreset.Morning, 100, 20)]
    [InlineData(TimePreset.Noon, 180, 60)]
    [InlineData(TimePreset.Evening, 260, 15)]
    public void Time_preset_sets_the_sun_and_keeps_the_rest(TimePreset preset, double azimuth, double elevation)
    {
        var before = new FlightConditions(WindSpeed: 5, WindFromDeg: 45, Turbulence: 0.4);
        var after = ConditionPresets.Apply(before, preset);
        Assert.Equal(before with { SunAzimuthDeg = azimuth, SunElevationDeg = elevation }, after);
        Assert.Equal(preset, ConditionPresets.MatchTime(after));
    }

    [Fact]
    public void Slider_rounding_still_matches_and_other_values_match_nothing()
    {
        // An HSlider with step 0.1 yields 3 × 0.1 = 0.30000000000000004.
        Assert.Equal(WindPreset.Breeze, ConditionPresets.MatchWind(new FlightConditions(WindSpeed: 3, Turbulence: 3 * 0.1)));
        Assert.Null(ConditionPresets.MatchWind(new FlightConditions(WindSpeed: 3.5, Turbulence: 0.3)));
        Assert.Null(ConditionPresets.MatchTime(new FlightConditions(SunAzimuthDeg: 200, SunElevationDeg: 40)));
    }

    [Fact]
    public void Keys_name_the_presets()
    {
        Assert.Equal("WIND_GUSTY", ConditionPresets.Key(WindPreset.Gusty));
        Assert.Equal("TIME_EVENING", ConditionPresets.Key(TimePreset.Evening));
    }
}
