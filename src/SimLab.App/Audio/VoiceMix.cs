using SimLab.App.Settings;

namespace SimLab.App.Audio;

/// <summary>Per-voice level multipliers (0..1) for <see cref="EngineSynth"/>: propeller, motor whine, wind and
/// rolling. Lets the sound screen and settings scale each voice independently of the frequency/gain physics.</summary>
public readonly record struct VoiceMix(double Propeller, double Motor, double Wind, double Rolling)
{
    public static readonly VoiceMix Full = new(1, 1, 1, 1);

    public static VoiceMix From(AudioSettings a) => new(
        Math.Clamp(a.Propeller, 0, 1),
        Math.Clamp(a.Motor, 0, 1),
        Math.Clamp(a.Wind, 0, 1),
        Math.Clamp(a.Rolling, 0, 1));
}
