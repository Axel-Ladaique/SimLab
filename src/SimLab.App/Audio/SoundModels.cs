using SimLab.Flight.Atmosphere;
using SimLab.Flight.Propulsion;

namespace SimLab.App.Audio;

/// <param name="ExhaustGain">Piston exhaust pulses at <paramref name="ShaftHz"/> (a single-cylinder two-stroke fires once per turn).</param>
/// <param name="RoarGain">Turbine jet roar (broadband).</param>
/// <param name="SpoolGain">Turbine spool whistle at <paramref name="ShaftHz"/>.</param>
public readonly record struct EngineVoice(double BladePassHz, double ShaftHz, double ElectricalHz, double PropGain, double WhineGain,
    double ExhaustGain = 0, double RoarGain = 0, double SpoolGain = 0);

/// <summary>Maps power-plant telemetry to the motor and propeller voice. Gains are 0..1, normalised by the static
/// full-throttle point.</summary>
public sealed class EngineSoundModel
{
    const double StoppedRpm = 50;
    const double PropIdleShare = 0.15;

    // A glow or gas engine's exhaust drowns its propeller: the prop voice is kept as a background swish.
    const double ExhaustIdleShare = 0.3, PistonPropShare = 0.45;
    const double TurbineWhineIdle = 0.3, TurbineWhineRange = 0.5, RoarIdleShare = 0.15;

    readonly SoundSpec _spec;
    readonly PowerSource _source;

    public EngineSoundModel(SoundSpec spec, double staticRpm, double staticThrust, double maxCurrent, PowerSource source = PowerSource.Electric)
    {
        _spec = spec;
        _source = source;
        StaticRpm = Math.Max(staticRpm, 1);
        StaticThrust = Math.Max(staticThrust, 1e-3);
        MaxCurrent = Math.Max(maxCurrent, 1e-3);
    }

    public double StaticRpm { get; }
    public double StaticThrust { get; }
    public double MaxCurrent { get; }

    public static EngineSoundModel For(PowerPlantSpec power, SoundSpec spec)
    {
        var full = new PowerPlant(power).SteadyState(1, 0, Isa.SeaLevelDensity);
        return new EngineSoundModel(spec, full.Rpm, full.Thrust, power.Motor?.MaxCurrentA ?? 0, power.Source);
    }

    public EngineVoice Evaluate(double rpm, double thrust, double motorCurrent)
    {
        double shaft = Math.Max(rpm, 0) / 60;
        if (rpm < StoppedRpm) return new EngineVoice(shaft * _spec.Blades, shaft, shaft * _spec.PolePairs, 0, 0);
        double thrustShare = Math.Clamp(thrust / StaticThrust, 0, 1);
        if (_source == PowerSource.Turbine)
            // No propeller: the spool whistles at the shaft rate and the roar follows thrust.
            return new EngineVoice(shaft * _spec.Blades, shaft, shaft * _spec.PolePairs, 0, 0,
                RoarGain: RoarIdleShare + (1 - RoarIdleShare) * thrustShare, SpoolGain: TurbineWhineIdle + TurbineWhineRange * thrustShare);
        double prop = PropIdleShare * Math.Min(rpm / StaticRpm, 1) + (1 - PropIdleShare) * Math.Clamp(thrust / StaticThrust, 0, 1);
        if (_source == PowerSource.Piston)
            return new EngineVoice(shaft * _spec.Blades, shaft, shaft * _spec.PolePairs, PistonPropShare * Math.Clamp(prop, 0, 1), 0,
                ExhaustGain: ExhaustIdleShare + (1 - ExhaustIdleShare) * thrustShare);
        double whine = Math.Sqrt(Math.Clamp(motorCurrent / MaxCurrent, 0, 1));
        return new EngineVoice(shaft * _spec.Blades, shaft, shaft * _spec.PolePairs, Math.Clamp(prop, 0, 1), whine);
    }
}

/// <summary>Aerodynamic rush: silent below 3 m/s, proportional to airspeed squared up to 30 m/s; brighter as speed rises.</summary>
public static class WindSoundModel
{
    const double Onset = 3, Full = 30;

    public static (double Gain, double CutoffHz) Evaluate(double airspeed)
    {
        double x = Math.Clamp((airspeed - Onset) / (Full - Onset), 0, 1);
        return (x * x, Math.Clamp(200 + 60 * airspeed, 200, 3000));
    }
}

/// <summary>Wheels or hull sliding on grass: needs a contact and ground speed, full at 15 m/s.</summary>
public static class RollingSoundModel
{
    public static double Gain(int pointsInContact, double groundSpeed) =>
        pointsInContact <= 0 ? 0 : Math.Clamp(groundSpeed / 15, 0, 1);
}
