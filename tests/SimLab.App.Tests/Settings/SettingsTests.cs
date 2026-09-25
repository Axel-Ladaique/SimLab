using SimLab.App.Settings;
using SimLab.Input;

namespace SimLab.App.Tests.Settings;

public sealed class SettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("simlab-settings-").FullName;

    [Fact]
    public void Missing_or_corrupt_settings_fall_back_to_defaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        Assert.Equal(new AppSettings(), AppSettings.Load(path));
        File.WriteAllText(path, "{ not json");
        Assert.Equal("fr", AppSettings.Load(path).Language);
    }

    [Fact]
    public void Unreadable_settings_fall_back_to_defaults()
    {
        if (OperatingSystem.IsWindows()) return; // relies on Unix file permissions
        var path = Path.Combine(_dir, "settings.json");
        new AppSettings { Language = "en" }.Save(path);
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            Assert.Equal(new AppSettings(), AppSettings.Load(path));
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void Unreadable_profile_reports_an_error_instead_of_throwing()
    {
        if (OperatingSystem.IsWindows()) return; // relies on Unix file permissions
        var store = new RadioProfileStore(_dir);
        store.Save(new RadioProfile { DeviceGuid = "locked", DeviceName = "TX16S" });
        File.SetUnixFileMode(store.PathFor("locked"), UnixFileMode.None);
        try
        {
            Assert.Null(store.Load("locked", out var error));
            Assert.NotNull(error);
        }
        finally
        {
            File.SetUnixFileMode(store.PathFor("locked"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void Settings_round_trip()
    {
        var path = Path.Combine(_dir, "nested", "settings.json");
        var s = new AppSettings { FovDeg = 35, AutoZoom = false, Language = "en", StickMode = StickMode.Mode1, LastAircraft = "wing",
            Conditions = new FlightConditions(WindSpeed: 5, WindFromDeg: 90, Turbulence: 0.5) };
        s.Save(path);
        var back = AppSettings.Load(path);
        Assert.Equal(s, back);
        Assert.Equal(5, back.Conditions.ToWindSettings().SpeedAt10m);
    }

    [Fact]
    public void Sanitize_clamps_fov_and_unknown_language()
    {
        var s = new AppSettings { FovDeg = 170, Language = "de" }.Sanitized();
        Assert.Equal(100, s.FovDeg);
        Assert.Equal("fr", s.Language);
    }

    [Fact]
    public void Audio_settings_default_round_trip_and_clamp()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "FovDeg": 50 }""");
        var loaded = AppSettings.Load(path);
        Assert.Equal(new AudioSettings(), loaded.Audio);

        var edited = new AudioSettings(Master: 0.3, Aircraft: 0.6, Ambience: 0, Propeller: 0.4, Motor: 0.7, Wind: 0.2, Rolling: 0.9, Impacts: 0.1);
        (loaded with { Audio = edited }).Save(path);
        var again = AppSettings.Load(path);
        Assert.Equal(edited, again.Audio);

        // Old settings files without an Audio block still load with the defaults.
        File.WriteAllText(path, """{ "FovDeg": 50 }""");
        Assert.Equal(new AudioSettings(), AppSettings.Load(path).Audio);

        // A pre-Audio-block file still has the old top-level MasterVolume/AircraftVolume/AmbienceVolume fields
        // (from before this task); dropping them must not throw, and they must not resurrect as the new Audio
        // block's values (the old fields are gone from AppSettings, so JSON deserialization just ignores them).
        File.WriteAllText(path, """{ "FovDeg": 50, "MasterVolume": 0.3, "AircraftVolume": 0.6, "AmbienceVolume": 0.0 }""");
        Assert.Equal(new AudioSettings(), AppSettings.Load(path).Audio);

        var clamped = (new AppSettings() with { Audio = new AudioSettings(Master: 3, Ambience: -1, Wind: 2, Rolling: -5) }).Sanitized();
        Assert.Equal((1.0, 0.0, 1.0, 0.0), (clamped.Audio.Master, clamped.Audio.Ambience, clamped.Audio.Wind, clamped.Audio.Rolling));
    }

    [Fact]
    public void Radio_profiles_are_stored_per_guid()
    {
        var store = new RadioProfileStore(_dir);
        var profile = new RadioProfile
        {
            DeviceGuid = "0300/abc:def",
            DeviceName = "TX16S",
            Channels = { [StickFunction.Aileron] = new ChannelSettings(3, false, AxisCalibration.Identity) },
        };
        store.Save(profile);
        Assert.DoesNotContain(':', Path.GetFileName(store.PathFor(profile.DeviceGuid)));
        var back = store.Load(profile.DeviceGuid, out var error);
        Assert.Null(error);
        Assert.Equal(3, back!.Channels[StickFunction.Aileron].AxisIndex);
        Assert.Null(store.Load("unknown", out _));
    }

    [Fact]
    public void Corrupt_profile_reports_an_error_instead_of_throwing()
    {
        var store = new RadioProfileStore(_dir);
        File.WriteAllText(store.PathFor("bad"), "{ \"Channels\": { \"Aileron\": { \"AxisIndex\": -2 } } }");
        Assert.Null(store.Load("bad", out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Set_reversed_saves_the_flipped_channel()
    {
        var store = new RadioProfileStore(_dir);
        store.Save(new RadioProfile
        {
            DeviceGuid = "tx16s",
            Channels = { [StickFunction.Rudder] = new ChannelSettings(3, true, AxisCalibration.Identity) },
            Switches = { new SwitchBinding(SwitchAction.Reset, ButtonIndex: 3) },
        });

        Assert.True(store.SetReversed("tx16s", StickFunction.Rudder, false));

        var back = store.Load("tx16s", out _)!;
        Assert.False(back.Channels[StickFunction.Rudder].Reversed);
        Assert.Single(back.Switches);
    }

    [Fact]
    public void Set_reversed_without_a_profile_or_channel_returns_false()
    {
        var store = new RadioProfileStore(_dir);
        Assert.False(store.SetReversed("missing", StickFunction.Rudder, true));
        store.Save(new RadioProfile { DeviceGuid = "g" });
        Assert.False(store.SetReversed("g", StickFunction.Rudder, true));
    }

    [Fact]
    public void Fullscreen_and_field_default_round_trip_and_sanitize()
    {
        var defaults = new AppSettings();
        Assert.True(defaults.Fullscreen);
        Assert.Equal("club", defaults.LastField);

        var path = Path.Combine(_dir, "settings.json");
        (defaults with { Fullscreen = false }).Save(path);
        Assert.False(AppSettings.Load(path).Fullscreen);

        File.WriteAllText(path, """{ "LastField": "moon" }""");
        Assert.Equal("club", AppSettings.Load(path).LastField);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
