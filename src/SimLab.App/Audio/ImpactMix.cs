namespace SimLab.App.Audio;

/// <summary>Linear volume for an impact one-shot: the event's own intensity (0..1, quieter bounces are still
/// audible at a quarter volume) scaled by the user's impacts volume.</summary>
public static class ImpactMix
{
    public static double Linear(double intensity, double impactsVolume) =>
        Math.Clamp(0.25 + 0.75 * intensity, 0, 1) * Math.Clamp(impactsVolume, 0, 1);
}
