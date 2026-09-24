using SimLab.Flight.Atmosphere;
using SimLab.Flight.Propulsion;

namespace SimLab.App.Audio;

public readonly record struct EngineVoice(double BladePassHz, double ShaftHz, double ElectricalHz, double PropGain, double WhineGain);

/// <summary>Maps power-plant telemetry to the motor and propeller voice. Gains are 0..1, normalised by the static
/// full-throttle point.</summary>
public sealed class EngineSoundModel
{
    const double StoppedRpm = 50;
    const double PropIdleShare = 0.15;

    readonly SoundSpec _spec;

    public EngineSoundModel(SoundSpec spec, double staticRpm, double staticThrust, double maxCurrent)
    {
        _spec = spec;
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
        return new EngineSoundModel(spec, full.Rpm, full.Thrust, power.Motor.MaxCurrentA);
    }

    public EngineVoice Evaluate(double rpm, double thrust, double motorCurrent)
    {
        double shaft = Math.Max(rpm, 0) / 60;
        if (rpm < StoppedRpm) return new EngineVoice(shaft * _spec.Blades, shaft, shaft * _spec.PolePairs, 0, 0);
        double prop = PropIdleShare * Math.Min(rpm / StaticRpm, 1) + (1 - PropIdleShare) * Math.Clamp(thrust / StaticThrust, 0, 1);
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
