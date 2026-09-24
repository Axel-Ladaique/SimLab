namespace SimLab.Input;

public enum StickFunction { Throttle, Aileron, Elevator, Rudder }

/// <param name="Expo">0 = linear, 1 = fully cubic (EdgeTX-style).</param>
/// <param name="Rate">Output scale (dual rate), 0..1.</param>
public sealed record ChannelSettings(
    int AxisIndex,
    bool Reversed,
    AxisCalibration Calibration,
    double Trim = 0,
    double Expo = 0,
    double Rate = 1);

/// <summary>Throttle 0..1; aileron, elevator, rudder −1..1 (right, pitch up, right positive).</summary>
public readonly record struct StickState(double Throttle, double Aileron, double Elevator, double Rudder)
{
    public static readonly StickState Idle = new(0, 0, 0, 0);
}

public static class ChannelPipeline
{
    public static double Process(double raw, ChannelSettings s)
    {
        double v = s.Calibration.Normalize(raw);
        if (s.Reversed) v = -v;
        v = Math.Clamp(v + s.Trim, -1, 1);
        v = ApplyExpo(v, s.Expo);
        return Math.Clamp(v * s.Rate, -1, 1);
    }

    public static double ApplyExpo(double x, double expo)
    {
        double k = Math.Clamp(expo, 0, 1);
        return k * x * x * x + (1 - k) * x;
    }
}
