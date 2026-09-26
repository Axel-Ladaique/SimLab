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
    const double ImpactAt = 11.5;
    const double WindLow = 5, WindHigh = 25;
    const double RollGroundSpeed = 8;

    /// <param name="t">Time within the loop; wraps modulo <see cref="LoopSeconds"/>.</param>
    /// <param name="engine">The previewed aircraft's engine model (its static RPM/thrust/current point).</param>
    /// <param name="spec">The previewed aircraft's sound spec (blades, pole pairs).</param>
    public static SynthParams At(double t, EngineSoundModel engine, SoundSpec spec)
    {
        // spec is unused directly: `engine` already reads blades/pole pairs from it via EngineSoundModel.For/ctor.
        // A degenerate model (StaticRpm <= 0) would divide by zero below; play it safe and go silent instead of
        // producing NaN (EngineSoundModel's own constructor already clamps StaticRpm >= 1, but this guard keeps
        // the preview robust even if that ever changes).
        if (engine.StaticRpm <= 0) return SynthParams.Silent;

        double loop = Wrap(t, LoopSeconds);

        double rpm = Rpm(loop, engine.StaticRpm);
        double thrust = engine.StaticThrust * Square(rpm / engine.StaticRpm);
        double current = engine.MaxCurrent * Cube(rpm / engine.StaticRpm);
        var voice = engine.Evaluate(rpm, thrust, current);

        double airspeed = Airspeed(loop);
        var (windGain, cutoff) = WindSoundModel.Evaluate(airspeed);

        bool rolling = loop is >= WindEnd and < RollEnd;
        double rollGain = RollingSoundModel.Gain(rolling ? 1 : 0, rolling ? RollGroundSpeed : 0);

        return new SynthParams(voice.BladePassHz, voice.ShaftHz, voice.ElectricalHz, voice.PropGain, voice.WhineGain,
            windGain, cutoff, rollGain, voice.ExhaustGain, voice.RoarGain, voice.SpoolGain);
    }

    /// <summary>Whether the loop's one impact instant (t = 11.5 s modulo <see cref="LoopSeconds"/>) was crossed
    /// while advancing the preview clock from <paramref name="t0"/> to <paramref name="t1"/> (exclusive/inclusive:
    /// the impact at exactly <paramref name="t1"/> counts, one at exactly <paramref name="t0"/> does not — so
    /// consecutive calls that chain <c>t1</c> into the next call's <c>t0</c> partition the timeline without ever
    /// double-counting or skipping an instant). Sampling <c>At(t).Impact</c> inside a fixed window instead would
    /// miss the impact whenever a frame is slow enough to jump over that window (e.g. at low frame rates or after
    /// a hitch); this instead looks at whether the impact instant lies anywhere in the elapsed span, however long
    /// that span is. <paramref name="t0"/> and <paramref name="t1"/> are the raw, never-wrapped preview clock (so
    /// a step that carries the clock past a multiple of <see cref="LoopSeconds"/> is handled automatically, with
    /// no separate wrap-around case). A span of zero or negative length never crosses anything. A span of a full
    /// loop or longer is guaranteed to contain at least one impact instant and reports it as a single crossing
    /// (this is a yes/no "did an impact happen", not a count of how many); a shorter span can contain at most one
    /// instant, since consecutive instants are exactly <see cref="LoopSeconds"/> apart.</summary>
    public static bool ImpactBetween(double t0, double t1)
    {
        double span = t1 - t0;
        if (span <= 0) return false;
        if (span >= LoopSeconds) return true;

        // The impact instants are T_n = ImpactAt + n * LoopSeconds for every integer n. Find the smallest one
        // strictly greater than t0, and check whether it still falls at or before t1.
        double n = System.Math.Floor((t0 - ImpactAt) / LoopSeconds) + 1;
        double nextImpact = ImpactAt + n * LoopSeconds;
        return nextImpact <= t1;
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
