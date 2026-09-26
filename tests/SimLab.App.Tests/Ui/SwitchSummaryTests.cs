using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.Flight.Controls;
using SimLab.Input;

namespace SimLab.App.Tests.Ui;

public class SwitchSummaryTests
{
    [Fact]
    public void Source_is_named_by_kind_and_one_based_number()
    {
        Assert.Equal(("RADIO_SOURCE_AXIS", 6), (SwitchSummary.SourceKey(new SwitchSource(AxisIndex: 5)), SwitchSummary.SourceNumber(new SwitchSource(AxisIndex: 5))));
        Assert.Equal(("RADIO_SOURCE_BUTTON", 1), (SwitchSummary.SourceKey(new SwitchSource(ButtonIndex: 0)), SwitchSummary.SourceNumber(new SwitchSource(ButtonIndex: 0))));
    }

    [Fact]
    public void Driven_by_lists_stick_channels_then_switch_functions_on_an_axis()
    {
        var p = new RadioProfile();
        p.Channels[StickFunction.Throttle] = new ChannelSettings(2, false, AxisCalibration.Identity);
        p.SetSwitch(new SwitchAssignment(SwitchFunction.Gear, new SwitchSource(AxisIndex: 5), [new(-1, 0), new(1, 1)]));
        p.SetSwitch(new SwitchAssignment(SwitchFunction.Flaps, new SwitchSource(AxisIndex: 5), [new(-1, 0), new(1, 2)]));
        Assert.Equal(["STICK_THROTTLE"], SwitchSummary.DrivenBy(p, 2));
        Assert.Equal(["SWITCH_GEAR", "SWITCH_FLAPS"], SwitchSummary.DrivenBy(p, 5));
        Assert.Empty(SwitchSummary.DrivenBy(p, 0));
    }

    [Fact]
    public void Status_keys_follow_gear_flaps_and_throttle_cut()
    {
        var controls = new ControlInputs(0, 0, 0, 0, FlapSetting.Landing) { GearUp = true, ThrottleCut = true };
        Assert.Equal(["SWITCH_GEAR_UP", "SWITCH_FLAPS_LANDING", "SWITCH_THROTTLE_CUT_CUT"], SwitchSummary.Status(controls));
        Assert.Equal(["SWITCH_GEAR_DOWN", "SWITCH_FLAPS_TAKEOFF", "SWITCH_THROTTLE_CUT_ARMED"],
            SwitchSummary.Status(new ControlInputs(0, 0, 0, 0, FlapSetting.Half)));
    }
}
