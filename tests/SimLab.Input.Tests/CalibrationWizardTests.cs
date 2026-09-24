namespace SimLab.Input.Tests;

public class CalibrationWizardTests
{
    // Physical layout: axis0 = rudder, axis1 = throttle (reversed: idle = +1), axis2 = elevator, axis3 = aileron, axis4 = noisy unused.
    static RawInputFrame Frame(double a0, double a1, double a2, double a3, double a4 = 0) => new([a0, a1, a2, a3, a4], []);

    static void FeedMany(CalibrationWizard w, RawInputFrame f, int n = 10)
    {
        for (int i = 0; i < n; i++) w.Feed(f);
    }

    static CalibrationWizard Calibrated()
    {
        var w = new CalibrationWizard(5);
        FeedMany(w, Frame(0, 1, 0, 0, 0.02));
        w.Next();
        foreach (var v in new[] { -1.0, 1.0 })
        {
            FeedMany(w, Frame(v, 1, 0, 0, -0.03));
            FeedMany(w, Frame(0, v, 0, 0, 0.03));
            FeedMany(w, Frame(0, 1, v, 0));
            FeedMany(w, Frame(0, 1, 0, v));
        }
        w.Next();
        Assert.Equal(StickFunction.Throttle, w.FunctionToIdentify);
        FeedMany(w, Frame(0, -1, 0, 0)); w.Next();
        Assert.Equal(StickFunction.Aileron, w.FunctionToIdentify);
        FeedMany(w, Frame(0, -1, 0, 1)); w.Next();
        FeedMany(w, Frame(0, -1, 0.9, 0)); w.Next();
        FeedMany(w, Frame(1, -1, 0, 0)); w.Next();
        return w;
    }

    [Fact]
    public void Full_calibration_identifies_shuffled_and_reversed_axes()
    {
        var w = Calibrated();
        Assert.Equal(CalibrationWizard.Stage.Done, w.Current);
        var p = w.BuildProfile("guid-1", "TX16S");

        Assert.Equal(1, p.Channels[StickFunction.Throttle].AxisIndex);
        Assert.True(p.Channels[StickFunction.Throttle].Reversed);
        Assert.Equal(3, p.Channels[StickFunction.Aileron].AxisIndex);
        Assert.Equal(2, p.Channels[StickFunction.Elevator].AxisIndex);
        Assert.Equal(0, p.Channels[StickFunction.Rudder].AxisIndex);
        Assert.False(p.Channels[StickFunction.Rudder].Reversed);

        var sticks = p.Read(Frame(0, -1, 0, 0.5));
        Assert.Equal(1.0, sticks.Throttle, 9);
        Assert.Equal(0.5, sticks.Aileron, 9);
        Assert.Equal(0.0, p.Read(Frame(0, 1, 0, 0)).Throttle, 9);
    }

    [Fact]
    public void Identification_fails_when_no_stick_moved_enough()
    {
        var w = new CalibrationWizard(4);
        FeedMany(w, new RawInputFrame([0, 0, 0, 0], []));
        w.Next();
        FeedMany(w, new RawInputFrame([1, 1, 1, 1], []));
        FeedMany(w, new RawInputFrame([-1, -1, -1, -1], []));
        w.Next();
        FeedMany(w, new RawInputFrame([0.1, 0, 0, 0], []));
        Assert.Throws<InvalidOperationException>(() => w.Next());
    }

    [Fact]
    public void Profile_cannot_be_built_before_the_end()
        => Assert.Throws<InvalidOperationException>(() => new CalibrationWizard(4).BuildProfile("g", "n"));

    [Fact]
    public void Center_step_needs_samples()
        => Assert.Throws<InvalidOperationException>(() => new CalibrationWizard(4).Next());
}
