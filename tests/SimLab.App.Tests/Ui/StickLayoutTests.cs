using SimLab.App.Settings;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.App.Tests.Ui;

public class StickLayoutTests
{
    static readonly StickState Sticks = new(Throttle: 1, Aileron: 0.5, Elevator: -0.25, Rudder: -1);

    [Fact]
    public void Mode_2_has_throttle_and_rudder_on_the_left()
    {
        var (left, right) = StickLayout.Place(Sticks, StickMode.Mode2);
        Assert.Equal(new StickPoint(-1, 1), left);
        Assert.Equal(new StickPoint(0.5, 0.25), right); // elevator −0.25 (pushed) is drawn up
    }

    [Fact]
    public void Mode_1_has_elevator_on_the_left_and_throttle_on_the_right()
    {
        var (left, right) = StickLayout.Place(Sticks, StickMode.Mode1);
        Assert.Equal(new StickPoint(-1, 0.25), left);
        Assert.Equal(new StickPoint(0.5, 1), right);
    }

    [Fact]
    public void Idle_throttle_sits_at_the_bottom()
        => Assert.Equal(-1, StickLayout.Place(new StickState(0, 0, 0, 0), StickMode.Mode2).Left.Y);

    [Fact]
    public void Center_and_extremes_show_both_sticks()
    {
        Assert.Equal(new StickGesture(true, true, false, new StickPoint(0, 0)),
            StickLayout.Gesture(CalibrationWizard.Stage.Center, null, StickMode.Mode2));
        Assert.Equal(new StickGesture(true, true, true, new StickPoint(0, 0)),
            StickLayout.Gesture(CalibrationWizard.Stage.Extremes, null, StickMode.Mode2));
    }

    [Theory]
    [InlineData(StickMode.Mode2, StickFunction.Throttle, true, 0, 1)]
    [InlineData(StickMode.Mode1, StickFunction.Throttle, false, 0, 1)]
    [InlineData(StickMode.Mode2, StickFunction.Aileron, false, 1, 0)]
    [InlineData(StickMode.Mode2, StickFunction.Elevator, false, 0, -1)]
    [InlineData(StickMode.Mode1, StickFunction.Elevator, true, 0, -1)]
    [InlineData(StickMode.Mode2, StickFunction.Rudder, true, 1, 0)]
    public void Identify_points_one_stick_to_its_positive_end(StickMode mode, StickFunction function, bool left, double x, double y)
        => Assert.Equal(new StickGesture(left, !left, false, new StickPoint(x, y)),
            StickLayout.Gesture(CalibrationWizard.Stage.Identify, function, mode));
}
