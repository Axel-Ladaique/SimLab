using SimLab.App.Cameras;
using SimLab.App.Session;
using SimLab.Flight.Sim;
using SimLab.Input;

namespace SimLab.App.Tests.Session;

public class InputTests
{
    static RadioProfile Profile(params SwitchAssignment[] switches)
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

    static SwitchAssignment OnAxis4(SwitchFunction f, params (double Value, int? State)[] positions) =>
        new(f, new SwitchSource(AxisIndex: 4), positions.Select(p => new SwitchPosition(p.Value, p.State)).ToList());

    static JoypadSnapshot Radio(double axis4, bool button1 = false) =>
        Pad("radio-1", [0, 0, -1, 0, axis4], [false, button1, false, false]);

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
    public void Latency_estimate_adds_processing_frame_and_interpolation_delay()
    {
        var meter = new LatencyMeter();
        meter.Add(0.001, 0.008, 0.5);
        Assert.Equal(1 + 8 + 0.5 * Simulation.FixedStep * 1000, meter.AverageMs, 9);
        Assert.Equal(0, new LatencyMeter().AverageMs);
    }

    [Fact]
    public void C_key_asks_for_the_next_camera_once_per_press()
    {
        var router = new InputRouter(_ => null);
        var c = new KeyboardCommands(false, false, false, NextCamera: true);
        Assert.Equal(new FlightCommand(FlightCommandKind.NextCamera), Assert.Single(router.Update(0.016, [], default, c).Commands));
        Assert.Empty(router.Update(0.016, [], default, c).Commands);
        Assert.Empty(router.Update(0.016, [], default, default).Commands);
        Assert.Equal(new FlightCommand(FlightCommandKind.NextCamera), Assert.Single(router.Update(0.016, [], default, c).Commands));
    }

    [Fact]
    public void G_key_toggles_the_gear_and_a_new_flight_starts_with_it_down()
    {
        var router = new InputRouter(_ => null);
        var g = new KeyboardCommands(false, false, false, ToggleGear: true);
        Assert.False(router.Update(0.016, [], default, default).Controls.GearUp);
        Assert.True(router.Update(0.016, [], default, g).Controls.GearUp);
        Assert.True(router.Update(0.016, [], default, g).Controls.GearUp);
        Assert.True(router.Update(0.016, [], default, default).Controls.GearUp);
        Assert.False(router.Update(0.016, [], default, g).Controls.GearUp);
        router.Update(0.016, [], default, default);
        router.Update(0.016, [], default, g);
        router.ResetForNewFlight();
        Assert.False(router.Update(0.016, [], default, default).Controls.GearUp);
    }

    [Fact]
    public void Reset_puts_the_keyboard_gear_back_down()
    {
        var router = new InputRouter(_ => null);
        router.Update(0.016, [], default, new KeyboardCommands(false, false, false, ToggleGear: true));
        var output = router.Update(0.016, [], default, new KeyboardCommands(Reset: true, false, false));
        Assert.Contains(new FlightCommand(FlightCommandKind.Reset), output.Commands);
        Assert.False(output.Controls.GearUp);
    }

    [Fact]
    public void F_key_steps_the_flaps_up_half_landing_and_back_up_and_reset_raises_them()
    {
        var router = new InputRouter(_ => null);
        var f = new KeyboardCommands(false, false, false, ToggleFlaps: true);
        Assert.Equal(0, router.Update(0.016, [], default, default).Controls.Flap);
        Assert.Equal(FlapSetting.Half, router.Update(0.016, [], default, f).Controls.Flap);
        Assert.Equal(FlapSetting.Half, router.Update(0.016, [], default, f).Controls.Flap);
        router.Update(0.016, [], default, default);
        Assert.Equal(FlapSetting.Landing, router.Update(0.016, [], default, f).Controls.Flap);
        router.Update(0.016, [], default, default);
        Assert.Equal(0, router.Update(0.016, [], default, f).Controls.Flap);
        router.Update(0.016, [], default, default);
        router.Update(0.016, [], default, f);
        Assert.Equal(0, router.Update(0.016, [], default, new KeyboardCommands(Reset: true, false, false)).Controls.Flap);
    }

    [Fact]
    public void Keyboard_commands_fire_once_per_press()
    {
        var router = new InputRouter(_ => null);
        var pause = new KeyboardCommands(false, true, false);
        Assert.Equal(new FlightCommand(FlightCommandKind.TogglePause), Assert.Single(router.Update(0.016, [], default, pause).Commands));
        Assert.Empty(router.Update(0.016, [], default, pause).Commands);
        var wind = new KeyboardCommands(false, false, true);
        Assert.Equal(new FlightCommand(FlightCommandKind.ToggleWind), Assert.Single(router.Update(0.016, [], default, wind).Commands));
    }

    [Fact]
    public void A_reset_button_fires_once_per_press_but_not_at_startup()
    {
        var reset = new SwitchAssignment(SwitchFunction.Reset, new SwitchSource(ButtonIndex: 1), [new(-1, null), new(1, SwitchStates.ResetFire)]);
        var router = new InputRouter(_ => Profile(reset));
        Assert.Empty(router.Update(0.016, [Radio(0, button1: true)], default, default).Commands);
        Assert.Empty(router.Update(0.016, [Radio(0)], default, default).Commands);
        Assert.Equal(new FlightCommand(FlightCommandKind.Reset), Assert.Single(router.Update(0.016, [Radio(0, button1: true)], default, default).Commands));
        Assert.Empty(router.Update(0.016, [Radio(0, button1: true)], default, default).Commands);
    }

    [Fact]
    public void A_gear_switch_holds_the_gear_and_the_g_key_only_acts_on_a_no_effect_position()
    {
        var gear = OnAxis4(SwitchFunction.Gear, (-1, SwitchStates.GearDown), (0, null), (1, SwitchStates.GearUp));
        var router = new InputRouter(_ => Profile(gear));
        var g = new KeyboardCommands(false, false, false, ToggleGear: true);
        Assert.False(router.Update(0.016, [Radio(-1)], default, g).Controls.GearUp);
        Assert.True(router.Update(0.016, [Radio(1)], default, default).Controls.GearUp);
        Assert.True(router.Update(0.016, [Radio(0)], default, default).Controls.GearUp);
        Assert.False(router.Update(0.016, [Radio(0)], default, g).Controls.GearUp);
    }

    [Fact]
    public void A_radio_without_a_gear_switch_leaves_the_gear_to_the_g_key()
    {
        var router = new InputRouter(_ => Profile());
        var g = new KeyboardCommands(false, false, false, ToggleGear: true);
        Assert.True(router.Update(0.016, [Radio(1)], default, g).Controls.GearUp);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(-0.6, 0.0)]
    [InlineData(0.0, FlapSetting.Half)]
    [InlineData(0.2, FlapSetting.Half)]
    [InlineData(1.0, FlapSetting.Landing)]
    public void A_three_position_flap_switch_gives_up_takeoff_or_landing(double axis, double flap)
    {
        var flaps = OnAxis4(SwitchFunction.Flaps, (-1, SwitchStates.FlapsUp), (0, SwitchStates.FlapsTakeoff), (1, SwitchStates.FlapsLanding));
        var router = new InputRouter(_ => Profile(flaps));
        Assert.Equal(flap, router.Update(0.016, [Radio(axis)], default, default).Controls.Flap);
    }

    [Fact]
    public void A_reversed_flap_switch_follows_its_learned_states()
    {
        var flaps = OnAxis4(SwitchFunction.Flaps, (-1, SwitchStates.FlapsLanding), (1, SwitchStates.FlapsUp));
        var router = new InputRouter(_ => Profile(flaps));
        Assert.Equal(FlapSetting.Landing, router.Update(0.016, [Radio(-1)], default, default).Controls.Flap);
        Assert.Equal(0, router.Update(0.016, [Radio(1)], default, default).Controls.Flap);
    }

    [Fact]
    public void Throttle_cut_forces_the_throttle_to_zero_and_flags_it()
    {
        var cut = OnAxis4(SwitchFunction.ThrottleCut, (-1, SwitchStates.ThrottleArmed), (1, SwitchStates.ThrottleCut));
        var router = new InputRouter(_ => Profile(cut));
        var full = Pad("radio-1", [0, 0, 1, 0, 1]);
        var output = router.Update(0.016, [full], default, default);
        Assert.Equal(0, output.Controls.Throttle);
        Assert.True(output.Controls.ThrottleCut);
        output = router.Update(0.016, [Pad("radio-1", [0, 0, 1, 0, -1])], default, default);
        Assert.Equal(1, output.Controls.Throttle, 9);
        Assert.False(output.Controls.ThrottleCut);
    }

    [Fact]
    public void A_new_flight_arms_the_throttle_again_unless_the_radio_says_cut()
    {
        var cut = OnAxis4(SwitchFunction.ThrottleCut, (-1, null), (1, SwitchStates.ThrottleCut));
        var router = new InputRouter(_ => Profile(cut));
        Assert.True(router.Update(0.016, [Radio(1)], default, default).Controls.ThrottleCut);
        router.ResetForNewFlight();
        Assert.False(router.Update(0.016, [Radio(-1)], default, default).Controls.ThrottleCut);
    }

    [Fact]
    public void Unplugging_the_radio_on_cut_falls_back_to_the_uncut_keyboard_throttle()
    {
        var cut = OnAxis4(SwitchFunction.ThrottleCut, (-1, SwitchStates.ThrottleArmed), (1, SwitchStates.ThrottleCut));
        var router = new InputRouter(_ => Profile(cut));
        Assert.True(router.Update(0.016, [Radio(1)], default, default).Controls.ThrottleCut);
        var up = default(KeyboardKeys) with { ThrottleUp = true };
        var output = router.Update(1.0, [], up, default);
        Assert.False(output.Controls.ThrottleCut);
        Assert.True(output.Controls.Throttle > 0.4);
    }

    [Fact]
    public void Clearing_the_throttle_cut_assignment_uncuts_the_throttle()
    {
        var cut = OnAxis4(SwitchFunction.ThrottleCut, (-1, SwitchStates.ThrottleArmed), (1, SwitchStates.ThrottleCut));
        RadioProfile? profile = Profile(cut);
        var router = new InputRouter(_ => profile);
        Assert.True(router.Update(0.016, [Radio(1)], default, default).Controls.ThrottleCut);
        profile = Profile();
        router.InvalidateProfiles();
        Assert.False(router.Update(0.016, [Radio(1)], default, default).Controls.ThrottleCut);
    }

    [Fact]
    public void A_no_effect_throttle_cut_position_keeps_the_last_state()
    {
        var cut = OnAxis4(SwitchFunction.ThrottleCut, (-1, SwitchStates.ThrottleCut), (1, null));
        var router = new InputRouter(_ => Profile(cut));
        Assert.True(router.Update(0.016, [Radio(-1)], default, default).Controls.ThrottleCut);
        Assert.True(router.Update(0.016, [Radio(1)], default, default).Controls.ThrottleCut);
    }

    [Fact]
    public void Camera_osd_wind_and_pause_switches_set_their_state_at_startup_and_on_each_move()
    {
        var camera = OnAxis4(SwitchFunction.Camera, (-1, SwitchStates.CameraGround), (0, SwitchStates.CameraFpv), (1, SwitchStates.CameraChase));
        var osd = new SwitchAssignment(SwitchFunction.Osd, new SwitchSource(ButtonIndex: 1), [new(-1, SwitchStates.OsdOff), new(1, SwitchStates.OsdOn)]);
        var router = new InputRouter(_ => Profile(camera, osd));
        Assert.Equal([new FlightCommand(FlightCommandKind.SelectCamera, View: CameraView.Fpv), new FlightCommand(FlightCommandKind.SetOsd, On: false)],
            router.Update(0.016, [Radio(0)], default, default).Commands);
        Assert.Equal([new FlightCommand(FlightCommandKind.SelectCamera, View: CameraView.Chase), new FlightCommand(FlightCommandKind.SetOsd, On: true)],
            router.Update(0.016, [Radio(1, button1: true)], default, default).Commands);
    }

    [Theory]
    [InlineData(SwitchFunction.Wind, SwitchStates.WindOn, FlightCommandKind.SetWind, true)]
    [InlineData(SwitchFunction.Wind, SwitchStates.WindOff, FlightCommandKind.SetWind, false)]
    [InlineData(SwitchFunction.Pause, SwitchStates.PausePaused, FlightCommandKind.SetPause, true)]
    [InlineData(SwitchFunction.Pause, SwitchStates.PauseRunning, FlightCommandKind.SetPause, false)]
    [InlineData(SwitchFunction.Osd, SwitchStates.OsdOn, FlightCommandKind.SetOsd, true)]
    public void Switch_events_map_to_setters(SwitchFunction f, int state, FlightCommandKind kind, bool on)
        => Assert.Equal(new FlightCommand(kind, On: on), FlightCommand.FromSwitch(new SwitchEvent(f, state)));

    [Fact]
    public void The_router_exposes_the_active_radio_switch_board()
    {
        var router = new InputRouter(_ => Profile(OnAxis4(SwitchFunction.Gear, (-1, 0), (1, 1))));
        Assert.Null(router.Switches);
        router.Update(0.016, [Radio(1)], default, default);
        Assert.Equal(1, router.Switches!.Position(SwitchFunction.Gear));
    }
}
