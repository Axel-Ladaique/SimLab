namespace SimLab.Input.Tests;

public class RadioProfileTests
{
    [Fact]
    public void Profile_round_trips_through_json()
    {
        var p = new RadioProfile
        {
            DeviceGuid = "030000001209000041ff000000000000",
            DeviceName = "FrSky TX16S",
            Channels = { [StickFunction.Elevator] = new ChannelSettings(1, true, new AxisCalibration(-0.9, 0.02, 0.95), Trim: 0.05, Expo: 0.3, Rate: 0.8) },
        };
        var back = RadioProfile.FromJson(p.ToJson());
        Assert.Equal(p.DeviceGuid, back.DeviceGuid);
        Assert.Equal(p.Channels[StickFunction.Elevator], back.Channels[StickFunction.Elevator]);
    }

    static string Profile(string channel) => $$"""
        { "DeviceGuid": "g", "DeviceName": "n", "Channels": { "Elevator": {{channel}} }, "Switches": [] }
        """;

    [Fact]
    public void Valid_json_profile_loads()
        => Assert.Equal(2, RadioProfile.FromJson(Profile("""{ "AxisIndex": 2, "Reversed": false, "Calibration": { "Min": -1, "Center": 0, "Max": 1 } }""")).Channels[StickFunction.Elevator].AxisIndex);

    [Theory]
    [InlineData("""{ "AxisIndex": -1, "Reversed": false, "Calibration": { "Min": -1, "Center": 0, "Max": 1 } }""")]
    [InlineData("""{ "AxisIndex": 1, "Reversed": false }""")]
    [InlineData("""{ "AxisIndex": 1, "Reversed": false, "Calibration": null }""")]
    [InlineData("""{ "AxisIndex": 1, "Reversed": false, "Calibration": { "Min": 0.5, "Center": 0, "Max": 1 } }""")]
    [InlineData("""{ "AxisIndex": 1, "Reversed": false, "Calibration": { "Min": -1, "Center": 0.8, "Max": 0.5 } }""")]
    [InlineData("null")]
    public void Invalid_channel_settings_are_rejected(string channel)
        => Assert.Throws<InvalidDataException>(() => RadioProfile.FromJson(Profile(channel)));

    [Fact]
    public void Negative_axis_index_reads_as_an_unassigned_channel()
    {
        var p = new RadioProfile
        {
            Channels =
            {
                [StickFunction.Elevator] = new ChannelSettings(-1, false, AxisCalibration.Identity),
                [StickFunction.Throttle] = new ChannelSettings(-2, false, AxisCalibration.Identity),
            },
        };
        var sticks = p.Read(new RawInputFrame([0.5, 0.9], []));
        Assert.Equal(0.0, sticks.Elevator);
        Assert.Equal(0.0, sticks.Throttle);
    }

    [Fact]
    public void Set_reversed_flips_the_channel_and_keeps_its_other_settings()
    {
        var calibration = new AxisCalibration(-0.9, 0.02, 0.95);
        var p = new RadioProfile { Channels = { [StickFunction.Rudder] = new ChannelSettings(3, true, calibration, Trim: 0.1, Expo: 0.2, Rate: 0.9) } };
        var frame = new RawInputFrame([0, 0, 0, 0.5], []);
        double before = p.Read(frame).Rudder;

        Assert.True(p.SetReversed(StickFunction.Rudder, false));

        Assert.Equal(new ChannelSettings(3, false, calibration, Trim: 0.1, Expo: 0.2, Rate: 0.9), p.Channels[StickFunction.Rudder]);
        Assert.True(p.Read(frame).Rudder > 0);
        Assert.True(before < 0);
    }

    [Fact]
    public void Set_reversed_on_an_unassigned_channel_does_nothing()
    {
        var p = new RadioProfile();
        Assert.False(p.SetReversed(StickFunction.Throttle, true));
        Assert.Empty(p.Channels);
    }

    static SwitchAssignment Gear(int axis = 5) => new(SwitchFunction.Gear, new SwitchSource(AxisIndex: axis),
        [new(-1, SwitchStates.GearDown), new(0, null), new(1, SwitchStates.GearUp)]);

    static string WithSwitches(string switches) =>
        $$"""{ "DeviceGuid": "g", "DeviceName": "n", "Channels": {}, "Switches": {{switches}} }""";

    [Fact]
    public void Switch_assignments_round_trip()
    {
        var p = new RadioProfile { DeviceGuid = "g", Switches = { Gear() } };
        var back = RadioProfile.FromJson(p.ToJson()).Switches.Single();
        Assert.Equal(SwitchFunction.Gear, back.Function);
        Assert.Equal(new SwitchSource(AxisIndex: 5), back.Source);
        Assert.Equal(Gear().Positions, back.Positions);
    }

    [Fact]
    public void Set_switch_replaces_the_function_and_clear_removes_it()
    {
        var p = new RadioProfile();
        p.SetSwitch(Gear(5));
        p.SetSwitch(Gear(6));
        Assert.Equal(6, p.Switches.Single().Source.AxisIndex);
        p.ClearSwitch(SwitchFunction.Gear);
        Assert.Empty(p.Switches);
    }

    [Theory]
    [InlineData("""[ { "Function": "Gear", "Source": { "AxisIndex": 1 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] }, { "Function": "Gear", "Source": { "AxisIndex": 2 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] } ]""")]
    [InlineData("""[ { "Function": "Gear", "Source": { "AxisIndex": 1 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 2 } ] } ]""")]
    [InlineData("""[ { "Function": "Gear", "Source": { "AxisIndex": 1 }, "Positions": [ { "Value": -1, "State": 0 } ] } ]""")]
    [InlineData("""[ { "Function": "Gear", "Source": { }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] } ]""")]
    [InlineData("""[ { "Function": "Gear", "Source": { "AxisIndex": 1, "ButtonIndex": 1 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] } ]""")]
    [InlineData("""[ { "Function": "Gear", "Source": { "AxisIndex": -1 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] } ]""")]
    [InlineData("""[ { "Function": "Warp", "Source": { "AxisIndex": 1 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] } ]""")]
    public void Invalid_switch_assignments_are_rejected(string switches)
        => Assert.Throws<InvalidDataException>(() => RadioProfile.FromJson(WithSwitches(switches)));

    [Fact]
    public void Old_gear_axis_binding_becomes_down_below_and_up_above_the_threshold()
    {
        var s = RadioProfile.FromJson(WithSwitches("""[ { "Action": "GearUp", "AxisIndex": 4, "Threshold": 0.2 } ]""")).Switches.Single();
        Assert.Equal(SwitchFunction.Gear, s.Function);
        Assert.Equal(new SwitchSource(AxisIndex: 4), s.Source);
        Assert.Equal([new SwitchPosition(-0.3, SwitchStates.GearDown), new SwitchPosition(0.7, SwitchStates.GearUp)], s.Positions);
    }

    [Fact]
    public void Old_flap_axis_binding_becomes_three_positions_and_a_flap_button_two()
    {
        var axis = RadioProfile.FromJson(WithSwitches("""[ { "Action": "Flaps", "AxisIndex": 4, "Threshold": 0 } ]""")).Switches.Single();
        Assert.Equal([new SwitchPosition(-1, SwitchStates.FlapsUp), new SwitchPosition(0, SwitchStates.FlapsTakeoff),
            new SwitchPosition(1, SwitchStates.FlapsLanding)], axis.Positions);
        var button = RadioProfile.FromJson(WithSwitches("""[ { "Action": "Flaps", "ButtonIndex": 2 } ]""")).Switches.Single();
        Assert.Equal(new SwitchSource(ButtonIndex: 2), button.Source);
        Assert.Equal([new SwitchPosition(-1, SwitchStates.FlapsUp), new SwitchPosition(1, SwitchStates.FlapsLanding)], button.Positions);
    }

    [Fact]
    public void Old_reset_pause_and_wind_bindings_fire_or_turn_on_in_the_high_position_and_next_camera_is_dropped()
    {
        var profile = RadioProfile.FromJson(WithSwitches("""
            [ { "Action": "Reset", "ButtonIndex": 3 },
              { "Action": "Pause", "ButtonIndex": 4 },
              { "Action": "ToggleWind", "AxisIndex": 6, "Threshold": 0 },
              { "Action": "NextCamera", "ButtonIndex": 5 } ]
            """));
        Assert.Equal([SwitchFunction.Reset, SwitchFunction.Pause, SwitchFunction.Wind], profile.Switches.Select(s => s.Function));
        Assert.Equal([new SwitchPosition(-1, null), new SwitchPosition(1, SwitchStates.ResetFire)], profile.Switches[0].Positions);
        Assert.Equal([new SwitchPosition(-1, SwitchStates.PauseRunning), new SwitchPosition(1, SwitchStates.PausePaused)], profile.Switches[1].Positions);
        Assert.Equal([new SwitchPosition(-0.5, SwitchStates.WindOff), new SwitchPosition(0.5, SwitchStates.WindOn)], profile.Switches[2].Positions);
    }

    [Fact]
    public void A_converted_profile_saves_in_the_new_shape()
    {
        var json = RadioProfile.FromJson(WithSwitches("""[ { "Action": "GearUp", "ButtonIndex": 1 } ]""")).ToJson();
        Assert.DoesNotContain("\"Action\"", json);
        Assert.Contains("\"Positions\"", json);
    }
}
