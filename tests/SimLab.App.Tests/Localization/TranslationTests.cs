using SimLab.App.Localization;
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
        foreach (var k in new[] { "HUD_AIRSPEED", "HUD_ALTITUDE", "HUD_THROTTLE", "HUD_BATTERY", "HUD_TIMER",
                     "CAL_CENTER", "CAL_EXTREMES", "CAL_ID_THROTTLE", "CAL_ID_AILERON", "CAL_ID_ELEVATOR", "CAL_ID_RUDDER",
                     "CAL_DONE", "STICK_LEFT", "STICK_RIGHT" })
            Assert.Contains(k, keys);
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
                     "RADIO_REVERSE", "RADIO_REVERSE_TITLE", "RADIO_PREVIEW_TITLE", "RADIO_PREVIEW_AIRCRAFT" })
            Assert.Contains(k, keys);
    }
}
