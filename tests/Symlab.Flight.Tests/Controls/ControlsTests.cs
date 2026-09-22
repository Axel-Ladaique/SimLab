using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Controls;

public class ControlsTests
{
    [Fact]
    public void Elevon_mix_combines_aileron_and_elevator()
    {
        var right = new Dictionary<string, double> { ["aileron"] = -1, ["elevator"] = -1 };
        Assert.Equal(-0.8, ControlInputs.Mix(right, new ControlInputs(0, 0.3, 0.5, 0)), 12);
    }

    [Fact]
    public void Mix_is_clamped() =>
        Assert.Equal(-1, ControlInputs.Mix(new Dictionary<string, double> { ["aileron"] = -1, ["elevator"] = -1 }, new ControlInputs(0, 1, 1, 0)));

    [Fact]
    public void Unknown_channel_throws() => Assert.Throws<ArgumentException>(() => ControlInputs.Neutral.Get("gear"));

    [Theory]
    [InlineData("throttle", true)]
    [InlineData("flap", true)]
    [InlineData("Aileron", false)]
    public void Recognizes_channel_names(string name, bool expected) => Assert.Equal(expected, ControlInputs.IsChannel(name));

    [Fact]
    public void Deflection_uses_separate_limits_per_direction()
    {
        Assert.Equal(Angle.Rad(12), ControlMapping.CommandToDeflection(1, 12, 15), 12);
        Assert.Equal(-Angle.Rad(7.5), ControlMapping.CommandToDeflection(-0.5, 12, 15), 12);
    }

    [Fact]
    public void Servo_is_rate_limited()
    {
        var servo = new Servo(secondsPer60Deg: 0.1);
        servo.Step(Angle.Rad(30), 0.02);
        Assert.Equal(Angle.Rad(12), servo.Position, 9);
        for (int i = 0; i < 10; i++) servo.Step(Angle.Rad(30), 0.02);
        Assert.Equal(Angle.Rad(30), servo.Position, 9);
    }
}
