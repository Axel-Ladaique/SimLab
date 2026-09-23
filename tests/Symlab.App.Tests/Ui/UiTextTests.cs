using Symlab.App.Settings;
using Symlab.App.Ui;
using Symlab.Flight.Airframe;
using Symlab.Input;

namespace Symlab.App.Tests.Ui;

public class UiTextTests
{
    [Theory]
    [InlineData(StickFunction.Throttle, StickMode.Mode2, "STICK_LEFT")]
    [InlineData(StickFunction.Throttle, StickMode.Mode1, "STICK_RIGHT")]
    [InlineData(StickFunction.Elevator, StickMode.Mode2, "STICK_RIGHT")]
    [InlineData(StickFunction.Elevator, StickMode.Mode1, "STICK_LEFT")]
    [InlineData(StickFunction.Aileron, StickMode.Mode1, "STICK_RIGHT")]
    [InlineData(StickFunction.Rudder, StickMode.Mode2, "STICK_LEFT")]
    public void Stick_side_depends_on_the_mode(StickFunction f, StickMode mode, string expected)
        => Assert.Equal(expected, CalibrationPrompts.StickSideKey(f, mode));

    [Fact]
    public void Stage_keys_cover_every_stage()
    {
        Assert.Equal("CAL_CENTER", CalibrationPrompts.StageKey(CalibrationWizard.Stage.Center, null));
        Assert.Equal("CAL_ID_RUDDER", CalibrationPrompts.StageKey(CalibrationWizard.Stage.Identify, StickFunction.Rudder));
        Assert.Equal("CAL_DONE", CalibrationPrompts.StageKey(CalibrationWizard.Stage.Done, null));
    }

    [Fact]
    public void Flight_data_is_formatted_in_pilot_units()
    {
        var aircraft = new Aircraft(TestData.Aircraft("trainer"));
        var lines = FlightDataFormatter.Format(aircraft, heightAgl: 23.4, flightTimeSeconds: 201, throttle: 0.654);
        Assert.Equal(new[] { "HUD_AIRSPEED", "HUD_ALTITUDE", "HUD_THROTTLE", "HUD_BATTERY", "HUD_TIMER" }, lines.Select(l => l.Key));
        Assert.Equal("0 km/h", lines[0].Value);
        Assert.Equal("23 m", lines[1].Value);
        Assert.Equal("65 %", lines[2].Value);
        Assert.EndsWith(" V", lines[3].Value);
        Assert.Equal("03:21", lines[4].Value);
    }
}
