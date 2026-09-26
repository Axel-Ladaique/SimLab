using SimLab.App.Session;

namespace SimLab.App.Audio;

public readonly record struct SoundFrame(SynthParams Synth, double Rpm, IReadOnlyList<ImpactEvent> Impacts, bool Reset = false);

/// <summary>Reads a flight session once per rendered frame and produces smoothed synth parameters and impact events.</summary>
public sealed class AircraftSound
{
    readonly FlightSession _session;
    readonly EngineSoundModel? _engine;
    readonly ParameterSmoother _smoother = new();
    readonly ImpactDetector _impacts = new();
    int _resetCount;
    // Audio clock for the impact refractory times: advanced by the frame dt, so it stays monotonic when a wind toggle
    // restarts the simulation clock at 0.
    double _clock;

    public AircraftSound(FlightSession session, SoundSpec spec)
    {
        _session = session;
        Spec = spec;
        _resetCount = session.ResetCount;
        if (session.Definition.Power is { } power) _engine = EngineSoundModel.For(power, spec);
    }

    public SoundSpec Spec { get; }

    public SoundFrame Update(double dt)
    {
        var aircraft = _session.Aircraft;
        _clock += dt;
        bool reset = _session.ResetCount != _resetCount;
        if (reset)
        {
            _smoother.Reset(SynthParams.Silent);
            _impacts.Reset();
        }
        _resetCount = _session.ResetCount;

        var telemetry = aircraft.Power?.Telemetry;
        double rpm = telemetry?.Rpm ?? 0;
        var engine = _engine?.Evaluate(rpm, telemetry?.Thrust ?? 0, telemetry?.MotorCurrent ?? 0) ?? default;
        var (windGain, cutoff) = WindSoundModel.Evaluate(aircraft.AirData.Airspeed);
        var contacts = aircraft.Ground.Contacts(aircraft.State, _session.Terrain);
        int touching = contacts.Count(c => c.Depth > 0);
        var v = aircraft.State.Velocity;
        double groundSpeed = Math.Sqrt(v.X * v.X + v.Y * v.Y);

        var target = new SynthParams(engine.BladePassHz, engine.ShaftHz, engine.ElectricalHz, engine.PropGain, engine.WhineGain,
            windGain, cutoff, RollingSoundModel.Gain(touching, groundSpeed), engine.ExhaustGain, engine.RoarGain);
        var synth = _smoother.Update(target, dt);
        return new SoundFrame(synth, rpm, _impacts.Update(contacts, aircraft.Crash, _clock), reset);
    }
}
