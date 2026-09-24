using SimLab.App.Session;
using SimLab.Flight.Sim;
using SimLab.Input;

namespace SimLab.App.Tests.Session;

public class InputTests
{
    static RadioProfile Profile(params SwitchBinding[] switches)
    {
        var p = new RadioProfile { DeviceGuid = "radio-1", DeviceName = "TX16S" };
        p.Channels[StickFunction.Throttle] = new ChannelSettings(2, false, AxisCalibration.Identity);
        p.Channels[StickFunction.Aileron] = new ChannelSettings(0, false, AxisCalibration.Identity);
        p.Channels[StickFunction.Elevator] = new ChannelSettings(1, false, AxisCalibration.Identity);
        p.Channels[StickFunction.Rudder] = new ChannelSettings(3, false, AxisCalibration.Identity);
        p.Switches.AddRange(switches);
        return p;
    }

    static JoypadSnapshot Pad(string guid, double[] axes, bool[]? buttons = null) =>
        new(guid, "TX16S", new RawInputFrame(axes, buttons ?? new bool[4]));

    [Fact]
    public void Calibrated_radio_drives_the_aircraft()
    {
        var router = new InputRouter(guid => guid == "radio-1" ? Profile() : null);
        var output = router.Update(0.016, [Pad("radio-1", [0.5, -0.25, 1.0, 0])], default, default);
        Assert.Equal(InputSource.Radio, output.Source);
        Assert.Equal("TX16S", output.DeviceName);
        Assert.Equal(0.5, output.Controls.Aileron, 9);
        Assert.Equal(-0.25, output.Controls.Elevator, 9);
        Assert.Equal(1.0, output.Controls.Throttle, 9);
    }

    [Fact]
    public void Radio_output_carries_the_raw_frame_of_the_active_pad_and_keyboard_output_none()
    {
        var router = new InputRouter(guid => guid == "radio-1" ? Profile() : null);
        var radio = Pad("radio-1", [0.5, -0.25, 1.0, 0]);
        var output = router.Update(0.016, [Pad("other", [1, 1, 1, 1]), radio], default, default);
        Assert.Equal(radio.Frame, output.RawFrame);
        Assert.Null(router.Update(0.016, [Pad("other", [1, 1, 1, 1])], default, default).RawFrame);
    }

    [Fact]
    public void Reset_for_new_flight_idles_the_keyboard_throttle_and_reloads_profiles()
    {
        int loads = 0;
        var router = new InputRouter(_ => { loads++; return null; });
        var up = default(KeyboardKeys) with { ThrottleUp = true };
        router.Update(1.0, [Pad("radio-1", [0, 0, 0, 0])], up, default);
        Assert.True(router.Update(0.01, [], default, default).Controls.Throttle > 0.4);
        router.ResetForNewFlight();
        Assert.Equal(0.0, router.Update(0.01, [Pad("radio-1", [0, 0, 0, 0])], default, default).Controls.Throttle, 12);
        Assert.Equal(2, loads);
    }

    [Fact]
    public void Uncalibrated_pad_falls_back_to_the_keyboard()
    {
        var router = new InputRouter(_ => null);
        var output = router.Update(0.5, [Pad("other", [1, 1, 1, 1])], default(KeyboardKeys) with { RollRight = true }, default);
        Assert.Equal(InputSource.Keyboard, output.Source);
        Assert.True(output.Controls.Aileron > 0);
    }

    [Fact]
    public void Profiles_are_loaded_once_until_invalidated()
    {
        int loads = 0;
        var router = new InputRouter(_ => { loads++; return Profile(); });
        for (int i = 0; i < 5; i++) router.Update(0.016, [Pad("radio-1", [0, 0, 0, 0])], default, default);
        Assert.Equal(1, loads);
        router.InvalidateProfiles();
        router.Update(0.016, [Pad("radio-1", [0, 0, 0, 0])], default, default);
        Assert.Equal(2, loads);
    }

    [Fact]
    public void Radio_switches_and_keyboard_commands_fire_once_per_press()
    {
        var router = new InputRouter(_ => Profile(new SwitchBinding(SwitchAction.Reset, ButtonIndex: 1)));
        bool[] off = [false, false, false, false], on = [false, true, false, false];
        router.Update(0.016, [Pad("radio-1", [0, 0, 0, 0], off)], default, default);
        Assert.Equal(SwitchAction.Reset, Assert.Single(router.Update(0.016, [Pad("radio-1", [0, 0, 0, 0], on)], default, default).Actions));
        Assert.Empty(router.Update(0.016, [Pad("radio-1", [0, 0, 0, 0], on)], default, default).Actions);

        var pause = new KeyboardCommands(false, true, false);
        Assert.Equal(SwitchAction.Pause, Assert.Single(router.Update(0.016, [], default, pause).Actions));
        Assert.Empty(router.Update(0.016, [], default, pause).Actions);
    }

    [Fact]
    public void Switch_capture_binds_a_new_button_or_a_rising_axis()
    {
        var capture = new SwitchCapture();
        Assert.Null(capture.Update(SwitchAction.Reset, new RawInputFrame([0, -1], [false, false])));
        Assert.Null(capture.Update(SwitchAction.Reset, new RawInputFrame([0.2, -1], [false, false])));
        var byButton = capture.Update(SwitchAction.Reset, new RawInputFrame([0.2, -1], [false, true]));
        Assert.Equal(new SwitchBinding(SwitchAction.Reset, ButtonIndex: 1), byButton);

        var axisCapture = new SwitchCapture();
        axisCapture.Update(SwitchAction.ToggleWind, new RawInputFrame([0, -1], [false]));
        var byAxis = axisCapture.Update(SwitchAction.ToggleWind, new RawInputFrame([0, 1], [false]));
        Assert.Equal(new SwitchBinding(SwitchAction.ToggleWind, AxisIndex: 1, Threshold: 0), byAxis);
    }

    [Fact]
    public void Switch_capture_ignores_an_axis_moving_down()
    {
        var capture = new SwitchCapture();
        capture.Update(SwitchAction.Pause, new RawInputFrame([1], []));
        Assert.Null(capture.Update(SwitchAction.Pause, new RawInputFrame([-1], [])));
        Assert.Equal(new SwitchBinding(SwitchAction.Pause, AxisIndex: 0, Threshold: 0), capture.Update(SwitchAction.Pause, new RawInputFrame([1], [])));
    }

    [Fact]
    public void Latency_estimate_adds_processing_frame_and_interpolation_delay()
    {
        var meter = new LatencyMeter();
        meter.Add(0.001, 0.008, 0.5);
        Assert.Equal(1 + 8 + 0.5 * Simulation.FixedStep * 1000, meter.AverageMs, 9);
        Assert.Equal(0, new LatencyMeter().AverageMs);
    }
}
