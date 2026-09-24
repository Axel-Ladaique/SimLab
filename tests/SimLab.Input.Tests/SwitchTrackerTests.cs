namespace SimLab.Input.Tests;

public class SwitchTrackerTests
{
    static RawInputFrame Frame(bool button, double axis = 0) => new([0, 0, 0, 0, axis], [false, false, button]);

    [Fact]
    public void Fires_once_on_the_rising_edge()
    {
        var t = new SwitchTracker([new SwitchBinding(SwitchAction.Reset, ButtonIndex: 2)]);
        Assert.Empty(t.Update(Frame(false)));
        Assert.Equal(SwitchAction.Reset, Assert.Single(t.Update(Frame(true))));
        Assert.Empty(t.Update(Frame(true)));
        Assert.Empty(t.Update(Frame(false)));
        Assert.Equal(SwitchAction.Reset, Assert.Single(t.Update(Frame(true))));
    }

    [Fact]
    public void Switch_already_on_at_startup_does_not_fire()
    {
        var t = new SwitchTracker([new SwitchBinding(SwitchAction.Reset, ButtonIndex: 2)]);
        Assert.Empty(t.Update(Frame(true)));
    }

    [Fact]
    public void Axis_binding_fires_above_threshold()
    {
        var t = new SwitchTracker([new SwitchBinding(SwitchAction.ToggleWind, AxisIndex: 4, Threshold: 0.5)]);
        t.Update(Frame(false, -1));
        Assert.Empty(t.Update(Frame(false, 0.2)));
        Assert.Equal(SwitchAction.ToggleWind, Assert.Single(t.Update(Frame(false, 0.9))));
    }

    [Fact]
    public void Out_of_range_indices_are_ignored()
    {
        var t = new SwitchTracker([new SwitchBinding(SwitchAction.Pause, ButtonIndex: 12)]);
        t.Update(Frame(false));
        Assert.Empty(t.Update(Frame(true)));
    }
}
