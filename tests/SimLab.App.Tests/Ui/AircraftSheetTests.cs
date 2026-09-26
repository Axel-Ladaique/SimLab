using SimLab.App.Ui;

namespace SimLab.App.Tests.Ui;

public class AircraftSheetTests
{
    [Theory]
    [InlineData("trainer", 1.50, 1.48, 2.6, 0.525, TakeoffKind.Tricycle, 650, 4, 3000, 12, 6)]
    [InlineData("sport", 1.20, 1.32, 2.2, 0.336, TakeoffKind.TailDragger, 750, 4, 2600, 12, 6)]
    [InlineData("wing", 1.10, 0.56, 1.1, 0.2475, TakeoffKind.HandLaunch, 1400, 3, 2200, 7, 4)]
    public void Sheet_is_read_from_the_aircraft_files(string id, double span, double length, double mass, double area,
        TakeoffKind takeoff, double kv, int cells, double mah, double propDiameter, double propPitch)
    {
        var sheet = AircraftSheet.From(TestData.Aircraft(id));
        Assert.Equal(span, sheet.SpanM, 6);
        Assert.Equal(length, sheet.LengthM, 6);
        Assert.Equal(mass, sheet.MassKg, 6);
        Assert.Equal(area, sheet.WingAreaM2, 6);
        Assert.Equal(mass * 1000 / (area * 100), sheet.WingLoadingGPerDm2, 6);
        Assert.Equal(takeoff, sheet.Takeoff);
        Assert.NotNull(sheet.Power);
        Assert.Equal(kv, sheet.Power!.Kv, 6);
        Assert.Equal(cells, sheet.Power.Cells);
        Assert.Equal(mah, sheet.Power.CapacityMah, 6);
        Assert.Equal(propDiameter, sheet.Power.PropDiameterIn, 6);
        Assert.Equal(propPitch, sheet.Power.PropPitchIn, 6);
    }

    [Fact]
    public void Channels_come_from_the_mixes_and_the_power_plant()
    {
        Assert.Equal(new[] { SheetChannel.Ailerons, SheetChannel.Elevator, SheetChannel.Rudder, SheetChannel.Throttle },
            AircraftSheet.From(TestData.Aircraft("trainer")).Channels);
        Assert.Equal(new[] { SheetChannel.Elevons, SheetChannel.Throttle },
            AircraftSheet.From(TestData.Aircraft("wing")).Channels);
    }

    [Fact]
    public void Lines_are_formatted_for_the_menu()
    {
        var lines = AircraftSheet.From(TestData.Aircraft("trainer")).Lines(key => key);
        Assert.Equal(new SheetLine[]
        {
            new("SHEET_SPAN", "1.50 m"),
            new("SHEET_LENGTH", "1.48 m"),
            new("SHEET_MASS", "2.60 kg"),
            new("SHEET_WING_AREA", "52.5 dm²"),
            new("SHEET_WING_LOADING", "50 g/dm²"),
            new("SHEET_POWER", "650 kV · 4S 3000 mAh · 12×6 in"),
            new("SHEET_TAKEOFF", "TAKEOFF_TRICYCLE"),
            new("SHEET_CHANNELS", "CHANNEL_AILERONS, CHANNEL_ELEVATOR, CHANNEL_RUDDER, CHANNEL_THROTTLE"),
        }, lines);
    }

    [Fact]
    public void A_glider_says_so()
    {
        var glider = AircraftSheet.From(TestData.Aircraft("sport")) with { Power = null };
        Assert.Contains(new SheetLine("SHEET_POWER", "SHEET_GLIDER"), glider.Lines(key => key));
    }

    [Fact]
    public void Flaps_are_listed_as_a_channel()
    {
        Assert.Contains(SheetChannel.Flaps, AircraftSheet.From(TestData.Aircraft("p51")).Channels);
        Assert.DoesNotContain(SheetChannel.Flaps, AircraftSheet.From(TestData.Aircraft("f18")).Channels);
    }
}
