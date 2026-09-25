namespace SimLab.App.Audio;

/// <summary>Scripted 12-second loop exercising every voice of the aircraft sound (idle, full-power sweep, wind
/// pass, rolling pass, one impact) so the sound screen's "Listen" preview matches flight exactly: it reads the
/// same <see cref="EngineSoundModel"/>, <see cref="WindSoundModel"/> and <see cref="RollingSoundModel"/> that
/// <see cref="AircraftSound"/> uses in flight.</summary>
public static class SoundPreview
{
    public const double LoopSeconds = 12;

    const double IdleEnd = 1, RampUpEnd = 4, HoldEnd = 5, RampDownEnd = 7;
    const double WindMidT = 8.25, WindEnd = 9.5, RollEnd = 11.5;
    const double ImpactAt = 11.5, ImpactWindow = 1.0 / 30;
    const double WindLow = 5, WindHigh = 25;
    const double RollGroundSpeed = 8;

    /// <param name="t">Time within the loop; wraps modulo <see cref="LoopSeconds"/>.</param>
    /// <param name="engine">The previewed aircraft's engine model (its static RPM/thrust/current point).</param>
    /// <param name="spec">The previewed aircraft's sound spec (blades, pole pairs).</param>
    public static (SynthParams Params, bool Impact) At(double t, EngineSoundModel engine, SoundSpec spec)
    {
        // spec is unused directly: `engine` already reads blades/pole pairs from it via EngineSoundModel.For/ctor.
        double loop = Wrap(t, LoopSeconds);

        double rpm = Rpm(loop, engine.StaticRpm);
        double thrust = engine.StaticThrust * Square(rpm / engine.StaticRpm);
        double current = engine.MaxCurrent * Cube(rpm / engine.StaticRpm);
        var voice = engine.Evaluate(rpm, thrust, current);

        double airspeed = Airspeed(loop);
        var (windGain, cutoff) = WindSoundModel.Evaluate(airspeed);

        bool rolling = loop is >= WindEnd and < RollEnd;
        double rollGain = RollingSoundModel.Gain(rolling ? 1 : 0, rolling ? RollGroundSpeed : 0);

        var parameters = new SynthParams(voice.BladePassHz, voice.ShaftHz, voice.ElectricalHz, voice.PropGain, voice.WhineGain,
            windGain, cutoff, rollGain);
        bool impact = loop >= ImpactAt && loop < ImpactAt + ImpactWindow;
        return (parameters, impact);
    }

    static double Rpm(double loop, double staticRpm) => loop switch
    {
        < IdleEnd => 0,
        < RampUpEnd => staticRpm * (loop - IdleEnd) / (RampUpEnd - IdleEnd),
        < HoldEnd => staticRpm,
        < RampDownEnd => staticRpm * (1 - (loop - HoldEnd) / (RampDownEnd - HoldEnd)),
        _ => 0,
    };

    static double Airspeed(double loop)
    {
        if (loop < RampDownEnd || loop >= WindEnd) return 0;
        return loop < WindMidT
            ? Lerp(WindLow, WindHigh, (loop - RampDownEnd) / (WindMidT - RampDownEnd))
            : Lerp(WindHigh, WindLow, (loop - WindMidT) / (WindEnd - WindMidT));
    }

    static double Wrap(double t, double period)
    {
        double r = t % period;
        return r < 0 ? r + period : r;
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;
    static double Square(double x) => x * x;
    static double Cube(double x) => x * x * x;
}
