namespace SimLab.App.Settings;

/// <summary>Per-sound-source volumes, all 0..1. Master/Aircraft/Ambience drive the Godot buses; the four voice
/// volumes scale the synthesizer's voices (see <c>VoiceMix</c>); Impacts scales the impact one-shots (see
/// <c>ImpactMix</c>).</summary>
public sealed record AudioSettings(
    double Master = 0.8, double Aircraft = 1.0, double Ambience = 0.5,
    double Propeller = 1.0, double Motor = 1.0, double Wind = 1.0, double Rolling = 1.0, double Impacts = 1.0)
{
    public AudioSettings Sanitized() => this with
    {
        Master = Math.Clamp(Master, 0, 1),
        Aircraft = Math.Clamp(Aircraft, 0, 1),
        Ambience = Math.Clamp(Ambience, 0, 1),
        Propeller = Math.Clamp(Propeller, 0, 1),
        Motor = Math.Clamp(Motor, 0, 1),
        Wind = Math.Clamp(Wind, 0, 1),
        Rolling = Math.Clamp(Rolling, 0, 1),
        Impacts = Math.Clamp(Impacts, 0, 1),
    };
}
