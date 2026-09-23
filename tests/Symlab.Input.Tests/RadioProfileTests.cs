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
}
