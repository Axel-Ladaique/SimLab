using SimLab.Flight.Atmosphere;
using SimLab.Flight.Propulsion;

namespace SimLab.App.Audio;

/// <summary>The motor and propeller voice of an aircraft held still (zero airspeed, sea level) at a given throttle:
/// the power plant's steady state, voiced through the same <see cref="EngineSoundModel"/> as in flight. Used by the
/// main menu's live view; the wind and rolling voices stay silent.</summary>
public sealed class StaticRunUp
{
    readonly PowerPlant _plant;

    public StaticRunUp(PowerPlantSpec power, SoundSpec spec)
    {
        _plant = new PowerPlant(power);
        Engine = EngineSoundModel.For(power, spec);
    }

    public EngineSoundModel Engine { get; }

    /// <summary>Steady rpm, thrust and current at this throttle (0..1); all zero at idle.</summary>
    public SteadyStateResult At(double throttle) =>
        throttle > 0 ? _plant.SteadyState(Math.Min(throttle, 1), 0, Isa.SeaLevelDensity) : default;

    public SynthParams Synth(double throttle)
    {
        if (throttle <= 0) return SynthParams.Silent;
        var s = At(throttle);
        var v = Engine.Evaluate(s.Rpm, s.Thrust, s.Current);
        return SynthParams.Silent with
        {
            BladePassHz = v.BladePassHz, ShaftHz = v.ShaftHz, ElectricalHz = v.ElectricalHz,
            PropGain = v.PropGain, WhineGain = v.WhineGain,
        };
    }
}
