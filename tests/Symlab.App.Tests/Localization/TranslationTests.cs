using Symlab.App.Localization;
using Symlab.App.Ui;
using Symlab.Flight.Ground;

namespace Symlab.App.Tests.Localization;

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
}
