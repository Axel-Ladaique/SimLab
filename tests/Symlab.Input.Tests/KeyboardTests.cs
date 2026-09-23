namespace Symlab.Input.Tests;

public class KeyboardTests
{
    [Fact]
    public void Axis_ramps_toward_the_pressed_side_and_recenters()
    {
        var axis = new KeyboardAxis(rate: 2, returnRate: 4);
        axis.Update(0.25, negative: false, positive: true);
        Assert.Equal(0.5, axis.Value, 12);
        axis.Update(1.0, false, true);
        Assert.Equal(1.0, axis.Value, 12);
        axis.Update(0.1, false, false);
        Assert.Equal(0.6, axis.Value, 12);
        axis.Update(1.0, false, false);
        Assert.Equal(0.0, axis.Value, 12);
    }

    [Fact]
    public void Throttle_starts_at_idle_and_holds_its_position()
    {
        var stick = new KeyboardStick();
        Assert.Equal(0.0, stick.Update(0.01, default).Throttle, 12);
        var up = default(KeyboardKeys) with { ThrottleUp = true };
        stick.Update(1.0, up);
        double held = stick.Update(1.0, default).Throttle;
        Assert.True(held > 0.4, $"throttle {held}");
    }

    [Fact]
    public void Pitch_up_key_gives_positive_elevator()
        => Assert.True(new KeyboardStick().Update(0.2, default(KeyboardKeys) with { PitchUp = true }).Elevator > 0);
}
