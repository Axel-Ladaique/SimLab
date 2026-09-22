using Symlab.Flight.Geometry;

namespace Symlab.Flight.Controls;

/// <summary>Pilot commands. Throttle 0..1; others −1..1 with aileron + = roll right, elevator + = pitch up, rudder + = yaw right.</summary>
public readonly record struct ControlInputs(double Throttle, double Aileron, double Elevator, double Rudder, double Flap = 0)
{
    public static readonly ControlInputs Neutral = new(0, 0, 0, 0);

    static readonly HashSet<string> Channels = ["throttle", "aileron", "elevator", "rudder", "flap"];

    public static bool IsChannel(string name) => Channels.Contains(name);

    public double Get(string channel) => channel switch
    {
        "throttle" => Throttle,
        "aileron" => Aileron,
        "elevator" => Elevator,
        "rudder" => Rudder,
        "flap" => Flap,
        _ => throw new ArgumentException($"Unknown control channel '{channel}'."),
    };

    public static double Mix(IReadOnlyDictionary<string, double> mix, in ControlInputs inputs)
    {
        double sum = 0;
        foreach (var (channel, weight) in mix) sum += weight * inputs.Get(channel);
        return Math.Clamp(sum, -1, 1);
    }
}

public static class ControlMapping
{
    /// <summary>Maps a −1..1 command to a deflection in radians (positive = trailing edge down).</summary>
    public static double CommandToDeflection(double command, double maxPositiveDeg, double maxNegativeDeg) =>
        command >= 0 ? command * Angle.Rad(maxPositiveDeg) : command * Angle.Rad(maxNegativeDeg);
}
