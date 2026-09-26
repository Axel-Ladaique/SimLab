namespace SimLab.Input.Tests;

public class SwitchLearnerTests
{
    const double Dt = 0.02;

    static RawInputFrame F(double a0, double a1 = 0, bool b0 = false) => new([a0, a1], [b0]);

    static void Hold(SwitchLearner l, RawInputFrame frame, double seconds)
    {
        for (double t = 0; t < seconds; t += Dt) l.Feed(frame, Dt);
    }

    [Fact]
    public void Finds_a_three_position_axis_switch()
    {
        var l = new SwitchLearner();
        Hold(l, F(-1), 0.3);
        Hold(l, F(0), 0.3);
        Hold(l, F(1), 0.3);
        var r = l.Result()!;
        Assert.Equal(new SwitchSource(AxisIndex: 0), r.Source);
        Assert.Equal([-1.0, 0.0, 1.0], r.Positions);
    }

    [Fact]
    public void Positions_are_sorted_and_a_two_position_switch_gives_two()
    {
        var l = new SwitchLearner();
        Hold(l, F(0, 1), 0.3);
        Hold(l, F(0, -1), 0.3);
        Hold(l, F(0, 1), 0.3);
        var r = l.Result()!;
        Assert.Equal(new SwitchSource(AxisIndex: 1), r.Source);
        Assert.Equal([-1.0, 1.0], r.Positions);
    }

    [Fact]
    public void Passing_quickly_through_the_middle_is_not_a_position()
    {
        var l = new SwitchLearner();
        Hold(l, F(-1), 0.3);
        Hold(l, F(0), 0.1);
        Hold(l, F(1), 0.3);
        Assert.Equal([-1.0, 1.0], l.Result()!.Positions);
    }

    [Fact]
    public void Noise_within_the_band_still_counts_as_resting()
    {
        var l = new SwitchLearner();
        Hold(l, F(-1), 0.3);
        for (int i = 0; i < 20; i++) l.Feed(F(1 - (i % 2) * 0.03), Dt);
        Assert.Equal(2, l.Result()!.Positions.Count);
    }

    [Fact]
    public void Nothing_moved_gives_no_result()
    {
        var l = new SwitchLearner();
        Hold(l, F(0.3, -0.2), 1);
        Assert.Null(l.Result());
        Assert.Null(new SwitchLearner().Result());
    }

    [Fact]
    public void A_button_pressed_and_released_gives_minus_one_and_plus_one()
    {
        var l = new SwitchLearner();
        Hold(l, F(0), 0.1);
        Hold(l, F(0, b0: true), 0.1);
        Hold(l, F(0), 0.1);
        var r = l.Result()!;
        Assert.Equal(new SwitchSource(ButtonIndex: 0), r.Source);
        Assert.Equal([-1.0, 1.0], r.Positions);
    }

    [Fact]
    public void The_widest_travel_wins()
    {
        var l = new SwitchLearner();
        Hold(l, F(0, -1), 0.3);
        Hold(l, F(0.4, 1), 0.3);
        Assert.Equal(new SwitchSource(AxisIndex: 1), l.Result()!.Source);
    }

    [Fact]
    public void An_axis_resting_in_more_than_three_places_is_not_a_switch()
    {
        var l = new SwitchLearner();
        foreach (var v in new[] { -1.0, -0.5, 0, 0.5, 1 }) Hold(l, F(v), 0.3);
        Assert.Null(l.Result());
    }
}
