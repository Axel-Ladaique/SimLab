namespace SimLab.Input.Tests;

public class ChannelPipelineTests
{
    static readonly AxisCalibration Asymmetric = new(-0.6, 0.0, 0.8);

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.8, 1.0)]
    [InlineData(-0.6, -1.0)]
    [InlineData(0.4, 0.5)]
    [InlineData(-0.3, -0.5)]
    [InlineData(2.0, 1.0)]
    public void Calibration_normalizes_each_half_separately(double raw, double expected)
        => Assert.Equal(expected, Asymmetric.Normalize(raw), 12);

    [Fact]
    public void Reversed_channel_flips_the_sign()
        => Assert.Equal(-0.5, ChannelPipeline.Process(0.4, new ChannelSettings(0, true, Asymmetric)), 12);

    [Fact]
    public void Trim_offsets_and_output_is_clamped()
    {
        var s = new ChannelSettings(0, false, AxisCalibration.Identity, Trim: 0.1);
        Assert.Equal(0.1, ChannelPipeline.Process(0, s), 12);
        Assert.Equal(1.0, ChannelPipeline.Process(1, s), 12);
    }

    [Fact]
    public void Expo_keeps_endpoints_and_softens_the_center()
    {
        Assert.Equal(1.0, ChannelPipeline.ApplyExpo(1, 0.5), 12);
        Assert.Equal(-1.0, ChannelPipeline.ApplyExpo(-1, 0.5), 12);
        Assert.Equal(0.104, ChannelPipeline.ApplyExpo(0.2, 0.5), 12);
    }

    [Fact]
    public void Rate_scales_the_output()
        => Assert.Equal(0.6, ChannelPipeline.Process(1, new ChannelSettings(0, false, AxisCalibration.Identity, Rate: 0.6)), 12);

    [Fact]
    public void Profile_maps_throttle_to_zero_one_and_missing_channels_to_neutral()
    {
        var profile = new RadioProfile
        {
            Channels =
            {
                [StickFunction.Throttle] = new ChannelSettings(2, false, AxisCalibration.Identity),
                [StickFunction.Aileron] = new ChannelSettings(0, false, AxisCalibration.Identity),
            },
        };
        var sticks = profile.Read(new RawInputFrame([0.5, 0.9, 0.0], []));
        Assert.Equal(0.5, sticks.Throttle, 12);
        Assert.Equal(0.5, sticks.Aileron, 12);
        Assert.Equal(0.0, sticks.Elevator);
        Assert.Equal(0.0, sticks.Rudder);
    }

    [Fact]
    public void Missing_throttle_reads_as_idle()
        => Assert.Equal(0.0, new RadioProfile().Read(new RawInputFrame([], [])).Throttle);
}
