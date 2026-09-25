using SimLab.App.Audio;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;

namespace SimLab.App.Tests.Audio;

public class AircraftSoundTests
{
    static FlightSession Session(string id) => new(TestData.Aircraft(id), new FlightConditions(WindSpeed: 0));

    static SoundFrame Run(FlightSession session, AircraftSound sound, double seconds, ControlInputs input)
    {
        SoundFrame frame = default;
        for (double t = 0; t < seconds; t += 1.0 / 60)
        {
            session.Tick(1.0 / 60, input);
            frame = sound.Update(1.0 / 60);
        }
        return frame;
    }

    [Fact]
    public void Idle_on_the_runway_is_silent_and_full_power_is_loud()
    {
        using var session = Session("trainer");
        var sound = new AircraftSound(session, SoundSpec.Default);
        var idle = Run(session, sound, 1, ControlInputs.Neutral);
        Assert.True(idle.Synth.PropGain < 0.01 && idle.Synth.WindGain < 0.01);
        var full = Run(session, sound, 1.5, ControlInputs.Neutral with { Throttle = 1 });
        Assert.True(full.Synth.PropGain > 0.5, $"{full.Synth.PropGain}");
        double expectedBladePassHz = full.Rpm * 2 / 60;
        Assert.True(Math.Abs(full.Synth.BladePassHz - expectedBladePassHz) <= 0.02 * expectedBladePassHz,
            $"expected {expectedBladePassHz} within 2%, got {full.Synth.BladePassHz}");
        Assert.True(full.Synth.RollGain > 0, "rolling during the takeoff roll");
    }

    [Fact]
    public void Hand_launched_wing_has_wind_noise()
    {
        using var session = Session("wing");
        var sound = new AircraftSound(session, SoundSpec.Default);
        var f = Run(session, sound, 0.5, ControlInputs.Neutral);
        Assert.True(f.Synth.WindGain > 0);
    }

    /// <summary>Drops the aircraft from 3 m at 15 m/s and returns the impact kinds heard over one second of frames.</summary>
    static List<ImpactKind> ImpactsAfterDrop(FlightSession session, AircraftSound sound)
    {
        sound.Update(1.0 / 60);
        session.Aircraft.OverrideState(session.Aircraft.State with { Position = new Vec3(0, 0, 3), Velocity = new Vec3(0, 0, -15) });
        var kinds = new List<ImpactKind>();
        for (int i = 0; i < 60; i++)
        {
            session.Tick(1.0 / 60, ControlInputs.Neutral);
            kinds.AddRange(sound.Update(1.0 / 60).Impacts.Select(e => e.Kind));
        }
        Assert.NotEqual(CrashCause.None, session.Aircraft.Crash);
        return kinds;
    }

    [Fact]
    public void Reset_clears_smoothing_and_rearms_the_impact_events()
    {
        using var session = Session("trainer");
        var sound = new AircraftSound(session, SoundSpec.Default);
        Run(session, sound, 1.5, ControlInputs.Neutral with { Throttle = 1 });
        session.Reset();
        session.Tick(1.0 / 60, ControlInputs.Neutral);
        var f = sound.Update(1.0 / 60);
        Assert.True(f.Synth.PropGain < 0.05, $"{f.Synth.PropGain}");
        Assert.True(f.Reset, "the frame right after a reset must report Reset");
        session.Tick(1.0 / 60, ControlInputs.Neutral);
        Assert.False(sound.Update(1.0 / 60).Reset, "Reset must not stay set on later frames");

        var first = ImpactsAfterDrop(session, sound);
        Assert.Equal(1, first.Count(k => k == ImpactKind.Crash));
        Assert.Contains(first, k => k != ImpactKind.Crash);
        // The same drop after a reset (sim time jumps back) must sound the same: one crash again, and the
        // touchdown events must not be held back by the refractory time of the first drop.
        session.Reset();
        session.Tick(1.0 / 60, ControlInputs.Neutral);
        Assert.Equal(first, ImpactsAfterDrop(session, sound));
    }
}
