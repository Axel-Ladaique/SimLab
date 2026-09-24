namespace SimLab.App.Audio;

/// <summary>Everything the synthesizer needs for one frame. Frequencies in Hz, gains 0..1.</summary>
public readonly record struct SynthParams(
    double BladePassHz, double ShaftHz, double ElectricalHz,
    double PropGain, double WhineGain,
    double WindGain, double WindCutoffHz,
    double RollGain)
{
    public static readonly SynthParams Silent = new(0, 0, 0, 0, 0, 0, 200, 0);
}

/// <summary>One-pole smoothing of frame parameters: frequencies follow with the attack time, gains rise with the
/// attack time and fall with the (slower) release time. Exponential, so it never overshoots.</summary>
public sealed class ParameterSmoother
{
    readonly double _attack, _release;

    public ParameterSmoother(double attackSeconds = 0.03, double releaseSeconds = 0.12)
    {
        _attack = attackSeconds;
        _release = releaseSeconds;
    }

    public SynthParams Current { get; private set; } = SynthParams.Silent;

    public SynthParams Update(in SynthParams target, double dt)
    {
        var c = Current;
        Current = new SynthParams(
            Follow(c.BladePassHz, target.BladePassHz, _attack, dt),
            Follow(c.ShaftHz, target.ShaftHz, _attack, dt),
            Follow(c.ElectricalHz, target.ElectricalHz, _attack, dt),
            Gain(c.PropGain, target.PropGain, dt),
            Gain(c.WhineGain, target.WhineGain, dt),
            Gain(c.WindGain, target.WindGain, dt),
            Follow(c.WindCutoffHz, target.WindCutoffHz, _attack, dt),
            Gain(c.RollGain, target.RollGain, dt));
        return Current;
    }

    public void Reset(in SynthParams value) => Current = value;

    double Gain(double current, double target, double dt) => Follow(current, target, target > current ? _attack : _release, dt);

    static double Follow(double current, double target, double tau, double dt) =>
        current + (target - current) * (1 - Math.Exp(-dt / Math.Max(tau, 1e-6)));
}
