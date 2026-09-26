using SimLab.App.Localization;
using SimLab.App.Settings;
using SimLab.App.Ui;
using SimLab.App.Visual;
using SimLab.Flight.Ground;
using SimLab.Input;

namespace SimLab.App.Tests.Localization;

public class TranslationTests
{
    static TranslationTable Shipped() =>
        TranslationTable.Parse(File.ReadAllText(Path.Combine(TestData.RepoRoot, "game", "translations", "strings.csv")));

    [Fact]
    public void Parses_quoted_fields_escaped_quotes_and_newlines()
    {
        var t = TranslationTable.Parse("keys,fr,en\nA,\"un, deux\",\"say \"\"hi\"\"\"\nB,ligne\\nsuivante,next\n");
        Assert.Equal(new[] { "fr", "en" }, t.Languages);
        Assert.Equal("un, deux", t.Get("fr", "A"));
        Assert.Equal("say \"hi\"", t.Get("en", "A"));
        Assert.Equal("ligne\nsuivante", t.Get("fr", "B"));
        Assert.Equal("MISSING", t.Get("fr", "MISSING"));
    }

    [Fact]
    public void Shipped_file_has_french_first_and_no_missing_entries()
    {
        var t = Shipped();
        Assert.Equal("fr", t.Languages[0]);
        Assert.Empty(t.MissingEntries());
    }

    [Fact]
    public void Every_key_used_by_the_app_exists()
    {
        var t = Shipped();
        var keys = t.Keys.ToHashSet();
        foreach (CrashCause cause in Enum.GetValues<CrashCause>()) Assert.Contains(FlightDataFormatter.CrashKey(cause), keys);
        foreach (var k in new[] { "OSD_THROTTLE", "OSD_THROTTLE_CUT", "VIEW_GROUND", "VIEW_FPV", "VIEW_CHASE", "HUD_VIEW_BUTTON", "HUD_BUTTON",
                     "CAL_CENTER", "CAL_EXTREMES", "CAL_ID_THROTTLE", "CAL_ID_AILERON", "CAL_ID_ELEVATOR", "CAL_ID_RUDDER",
                     "CAL_DONE", "STICK_LEFT", "STICK_RIGHT" })
            Assert.Contains(k, keys);
        foreach (var f in SwitchStates.All)
        {
            Assert.Contains(SwitchStates.FunctionKey(f), keys);
            for (int s = 0; s < SwitchStates.Count(f); s++) Assert.Contains(SwitchStates.StateKey(f, s), keys);
        }
        foreach (var k in new[] { "RADIO_TITLE", "RADIO_DEVICE_UNCALIBRATED", "RADIO_CALIBRATE", "RADIO_NEXT", "RADIO_CANCEL",
                     "RADIO_SAVED", "RADIO_MODE", "RADIO_AXES", "RADIO_HELP", "CAL_FAILED", "RADIO_LEARN_PROMPT",
                     "RADIO_SOURCE_AXIS", "RADIO_SOURCE_BUTTON", "SWITCH_NO_EFFECT", "STICK_THROTTLE", "STICK_AILERON",
                     "STICK_ELEVATOR", "STICK_RUDDER", "RADIO_CHANNEL_FREE" })
            Assert.Contains(k, keys);
        foreach (var k in new[] { "RADIO_STEP_CONNECT", "RADIO_STEP_CALIBRATE", "RADIO_STEP_SWITCHES", "RADIO_BACK",
                     "RADIO_PILL_READY", "RADIO_PILL_TODO", "RADIO_EMPTY_TITLE", "RADIO_EMPTY_BODY", "RADIO_CONTINUE",
                     "RADIO_START_CALIBRATION", "RADIO_CAL_PROGRESS", "RADIO_DETAILS", "RADIO_SAVE", "RADIO_LEARN_TITLE",
                     "RADIO_LEARN_WAITING", "RADIO_POSITION", "RADIO_ADD_SWITCH", "RADIO_CALIBRATED_HINT",
                     "RADIO_CAL_UNPLUGGED", "RADIO_CHIP_HINT" })
            Assert.Contains(k, keys);
    }

    [Fact]
    public void The_tabbed_radio_screen_keys_are_gone()
    {
        var keys = Shipped().Keys.ToHashSet();
        foreach (var k in new[] { "RADIO_TAB_RADIO", "RADIO_TAB_CHANNELS", "RADIO_TAB_SWITCHES", "RADIO_LEARN", "RADIO_CLEAR",
                     "RADIO_LEARN_DONE", "RADIO_SWITCHES_HELP", "RADIO_LEARN_NONE", "RADIO_SWITCH_NONE", "RADIO_NO_DEVICE",
                     "RADIO_DEVICE_READY", "RADIO_PREVIEW_TITLE", "RADIO_PREVIEW_AIRCRAFT" })
            Assert.DoesNotContain(k, keys);
    }

    [Fact]
    public void Every_key_used_by_the_radio_screen_exists()
    {
        var keys = Shipped().Keys.ToHashSet();
        foreach (StickFunction f in Enum.GetValues<StickFunction>()) Assert.Contains(ControlCheck.ChannelKey(f), keys);
        foreach (CheckDirection d in Enum.GetValues<CheckDirection>())
        {
            Assert.Contains("CHECK_STICK_" + d.ToString().ToUpperInvariant(), keys);
            if (d == CheckDirection.Neutral) continue;
            Assert.Contains("CHECK_TE_" + d.ToString().ToUpperInvariant(), keys);
        }
        foreach (CheckEffect e in Enum.GetValues<CheckEffect>().Where(e => e != CheckEffect.None))
            Assert.Contains("CHECK_EFFECT_" + e.ToString().ToUpperInvariant(), keys);
        foreach (var k in new[] { "CHECK_STEER_LEFT", "CHECK_STEER_RIGHT", "CHECK_NO_SURFACE", "CHECK_WRONG_WAY",
                     "RADIO_REVERSE", "RADIO_REVERSE_TITLE" })
            Assert.Contains(k, keys);
    }

    [Fact]
    public void Every_condition_preset_has_a_name()
    {
        var keys = Shipped().Keys.ToHashSet();
        foreach (var p in Enum.GetValues<WindPreset>()) Assert.Contains(ConditionPresets.Key(p), keys);
        foreach (var p in Enum.GetValues<TimePreset>()) Assert.Contains(ConditionPresets.Key(p), keys);
    }

    [Fact]
    public void Every_key_used_by_the_main_menu_exists()
    {
        var keys = Shipped().Keys.ToHashSet();
        foreach (var k in new[] { "APP_TITLE", "MENU_FLY", "MENU_RADIO", "MENU_SOUND", "MENU_SETTINGS", "MENU_QUIT",
                     "MENU_AIRCRAFT", "MENU_FIELD", "MENU_WIND", "MENU_TIME", "MENU_CUSTOMIZE", "MENU_CUSTOMIZE_HIDE",
                     "MENU_LIVE_HINT", "COND_WIND_SPEED", "COND_WIND_DIR", "COND_TURBULENCE", "COND_SUN_AZIMUTH",
                     "COND_SUN_ELEVATION", "SET_FULLSCREEN" })
            Assert.Contains(k, keys);
        foreach (var field in SimLab.App.Maps.FieldCatalog.All) Assert.Contains(field.NameKey, keys);
    }

    [Fact]
    public void Every_aircraft_sheet_key_exists()
    {
        var keys = Shipped().Keys.ToHashSet();
        foreach (var line in AircraftSheet.From(TestData.Aircraft("trainer")).Lines(k => k)) Assert.Contains(line.Key, keys);
        Assert.Contains("SHEET_GLIDER", keys);
        foreach (var k in Enum.GetValues<TakeoffKind>()) Assert.Contains(AircraftSheet.TakeoffKey(k), keys);
        foreach (var c in Enum.GetValues<SheetChannel>()) Assert.Contains(AircraftSheet.ChannelKey(c), keys);
    }
}
