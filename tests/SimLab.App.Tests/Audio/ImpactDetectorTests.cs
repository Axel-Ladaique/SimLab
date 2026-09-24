using SimLab.App.Audio;
using SimLab.Flight.Ground;

namespace SimLab.App.Tests.Audio;

public class ImpactDetectorTests
{
    static ContactSample Wheel(double depth, double vn) => new("main", true, "wheel", depth, vn);
    static ContactSample Nose(double depth, double vn) => new("nose", false, "nose", depth, vn);

    [Fact]
    public void Touchdown_gives_one_gear_event_scaled_by_approach_speed()
    {
        var d = new ImpactDetector();
        Assert.Empty(d.Update([Wheel(-0.1, -2)], CrashCause.None, 0.00));
        var events = d.Update([Wheel(0.01, -2)], CrashCause.None, 0.02);
        var e = Assert.Single(events);
        Assert.Equal(ImpactKind.Gear, e.Kind);
        Assert.Equal(2 / ImpactDetector.FullApproachSpeed, e.Intensity, 9);
        Assert.Empty(d.Update([Wheel(0.02, -0.5)], CrashCause.None, 0.04));
    }

    [Fact]
    public void Resting_or_gentle_contact_is_silent()
    {
        var d = new ImpactDetector();
        for (int i = 0; i < 100; i++)
            Assert.Empty(d.Update([Wheel(0.005 + 0.001 * (i % 2), i % 2 == 0 ? -0.1 : 0.1)], CrashCause.None, i * 0.016));
    }

    [Fact]
    public void A_bounce_after_the_refractory_time_gives_a_second_event_but_chatter_does_not()
    {
        var d = new ImpactDetector();
        d.Update([Wheel(-0.1, -2)], CrashCause.None, 0);
        Assert.Single(d.Update([Wheel(0.01, -2)], CrashCause.None, 0.02));
        d.Update([Wheel(-0.01, 1)], CrashCause.None, 0.05);
        Assert.Empty(d.Update([Wheel(0.01, -1)], CrashCause.None, 0.10));
        d.Update([Wheel(-0.05, 1)], CrashCause.None, 0.30);
        Assert.Single(d.Update([Wheel(0.01, -1)], CrashCause.None, 0.50));
    }

    [Fact]
    public void Hull_contacts_are_hull_events_and_intensity_is_capped()
    {
        var d = new ImpactDetector();
        d.Update([Nose(-0.1, -9)], CrashCause.None, 0);
        var e = Assert.Single(d.Update([Nose(0.01, -9)], CrashCause.None, 0.02));
        Assert.Equal(ImpactKind.Hull, e.Kind);
        Assert.Equal(1, e.Intensity);
    }

    [Fact]
    public void A_new_crash_gives_one_full_crash_event()
    {
        var d = new ImpactDetector();
        d.Update([], CrashCause.None, 0);
        var e = Assert.Single(d.Update([], CrashCause.NoseOver, 0.02));
        Assert.Equal(new ImpactEvent(ImpactKind.Crash, 1), e);
        Assert.Empty(d.Update([], CrashCause.NoseOver, 0.04));
        d.Reset();
        Assert.Single(d.Update([], CrashCause.NoseOver, 0.06));
    }
}
