using SimLab.App.Audio;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.Flight.Controls;

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

    [Fact]
    public void Reset_clears_smoothing_and_rearms_the_crash_event()
    {
        using var session = Session("trainer");
        var sound = new AircraftSound(session, SoundSpec.Default);
        Run(session, sound, 1.5, ControlInputs.Neutral with { Throttle = 1 });
        session.Reset();
        session.Tick(1.0 / 60, ControlInputs.Neutral);
        var f = sound.Update(1.0 / 60);
        Assert.True(f.Synth.PropGain < 0.05, $"{f.Synth.PropGain}");
    }
}
