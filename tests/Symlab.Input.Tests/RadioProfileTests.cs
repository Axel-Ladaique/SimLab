namespace Symlab.Input.Tests;

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
            Switches = { new SwitchBinding(SwitchAction.Reset, ButtonIndex: 3) },
        };
        var back = RadioProfile.FromJson(p.ToJson());
        Assert.Equal(p.DeviceGuid, back.DeviceGuid);
        Assert.Equal(p.Channels[StickFunction.Elevator], back.Channels[StickFunction.Elevator]);
        Assert.Equal(p.Switches[0], back.Switches[0]);
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
}
