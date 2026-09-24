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

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
