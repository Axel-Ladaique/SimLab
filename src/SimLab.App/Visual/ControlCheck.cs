using System.Globalization;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Input;

namespace SimLab.App.Visual;

public enum CheckDirection { Neutral, Up, Down, Left, Right }

public enum CheckEffect { None, RollRight, RollLeft, PitchUp, PitchDown, YawRight, YawLeft }

/// <param name="TrailingEdge">Where the control's trailing edge goes, in body axes (up/down/left/right).</param>
public readonly record struct SurfaceMove(string Control, CheckDirection TrailingEdge);

/// <param name="Turn">Which way the steered wheel turns the aircraft on the ground.</param>
public readonly record struct WheelMove(string Wheel, CheckDirection Turn);

/// <summary>What one stick channel does to one aircraft, as the simulator computes it.</summary>
/// <param name="Stick">Direction of the calibrated command (after reverse, trim, expo, rate).</param>
/// <param name="Effect">Dominant rotation of the moment the deflected surfaces produce.</param>
public sealed record ChannelCheck(
    StickFunction Channel,
    CheckDirection Stick,
    IReadOnlyList<SurfaceMove> Surfaces,
    IReadOnlyList<WheelMove> Wheels,
    CheckEffect Effect)
{
    /// <summary>The rotation a pilot expects from this stick direction.</summary>
    public CheckEffect Expected => (Channel, Stick) switch
    {
        (StickFunction.Aileron, CheckDirection.Right) => CheckEffect.RollRight,
        (StickFunction.Aileron, CheckDirection.Left) => CheckEffect.RollLeft,
        (StickFunction.Elevator, CheckDirection.Up) => CheckEffect.PitchUp,
        (StickFunction.Elevator, CheckDirection.Down) => CheckEffect.PitchDown,
        (StickFunction.Rudder, CheckDirection.Right) => CheckEffect.YawRight,
        (StickFunction.Rudder, CheckDirection.Left) => CheckEffect.YawLeft,
        _ => CheckEffect.None,
    };

    /// <summary>False when the surfaces rotate the aircraft some other way than the stick asks (a wrong mix).</summary>
    public bool Consistent => Surfaces.Count == 0 || Effect == Expected;
}

/// <summary>
/// Describes, per stick channel, which way the control surfaces and steered wheels move and which way that turns the
/// aircraft. Everything is derived from the aircraft's own mixing (<see cref="Aircraft.TargetDeflection"/>,
/// <see cref="Aircraft.SteerAngle"/>) and surface geometry, so a reversed radio channel or a wrong mix reads wrong.
/// </summary>
public static class ControlCheck
{
    /// <summary>Commands smaller than this read as a centred stick.</summary>
    public const double Deadband = 0.05;

    const double MinDeflection = 1e-6;

    public static ChannelCheck Describe(Aircraft aircraft, StickFunction channel, in ControlInputs inputs)
    {
        double command = channel switch
        {
            StickFunction.Aileron => inputs.Aileron,
            StickFunction.Elevator => inputs.Elevator,
            StickFunction.Rudder => inputs.Rudder,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), "The throttle has no surfaces to check."),
        };
        var stick = Math.Abs(command) < Deadband ? CheckDirection.Neutral : channel switch
        {
            StickFunction.Elevator => command > 0 ? CheckDirection.Up : CheckDirection.Down,
            _ => command > 0 ? CheckDirection.Right : CheckDirection.Left,
        };
        if (stick == CheckDirection.Neutral) return new ChannelCheck(channel, stick, [], [], CheckEffect.None);

        // Only this channel, so mixed surfaces (elevons) are attributed to each stick separately.
        var alone = channel switch
        {
            StickFunction.Aileron => new ControlInputs(0, command, 0, 0),
            StickFunction.Elevator => new ControlInputs(0, 0, command, 0),
            _ => new ControlInputs(0, 0, 0, command),
        };

        var definition = aircraft.Definition;
        var segments = aircraft.Aero.Segments;
        var surfaces = new List<SurfaceMove>();
        var moment = Vec3.Zero;
        for (int i = 0; i < definition.Controls.Count; i++)
        {
            double deflection = Aircraft.TargetDeflection(definition.Controls[i], alone);
            if (Math.Abs(deflection) < MinDeflection) continue;
            var trailingEdge = Vec3.Zero;
            foreach (var s in segments)
            {
                if (s.ControlIndex != i) continue;
                // Positive deflection = trailing edge toward −normal, adding lift along +normal (positions are CG-relative).
                trailingEdge += s.NormalAxis * (-Math.Sign(deflection) * s.Area);
                moment += Vec3.Cross(s.Position, s.NormalAxis * (deflection * s.Area));
            }
            surfaces.Add(new SurfaceMove(definition.Controls[i].Name, Classify(trailingEdge)));
        }

        var wheels = new List<WheelMove>();
        foreach (var w in definition.Wheels)
        {
            double steer = Aircraft.SteerAngle(w, alone);
            if (Math.Abs(steer) < MinDeflection) continue;
            bool ahead = Vec3.Dot(w.Position, BodyAxes.Forward) > 0;
            wheels.Add(new WheelMove(w.Name, (steer > 0) == ahead ? CheckDirection.Right : CheckDirection.Left));
        }

        return new ChannelCheck(channel, stick, surfaces, wheels, surfaces.Count == 0 ? CheckEffect.None : Effect(moment));
    }

    public static string Format(ChannelCheck check, Func<string, string> translate)
    {
        var text = $"{translate(ChannelKey(check.Channel))}: {translate("CHECK_STICK_" + Upper(check.Stick))}";
        if (check.Stick == CheckDirection.Neutral) return text;
        var parts = check.Surfaces.Select(s => $"{s.Control} {translate("CHECK_TE_" + Upper(s.TrailingEdge))}")
            .Concat(check.Wheels.Select(w => $"{w.Wheel} {translate("CHECK_STEER_" + Upper(w.Turn))}"))
            .ToList();
        if (parts.Count == 0) return $"{text} → {translate("CHECK_NO_SURFACE")}";
        text += $" → {string.Join(", ", parts)}";
        if (check.Effect != CheckEffect.None) text += $" → {translate("CHECK_EFFECT_" + Upper(check.Effect))}";
        if (!check.Consistent) text += $" {translate("CHECK_WRONG_WAY")}";
        return text;
    }

    public static string FormatThrottle(double throttle, Func<string, string> translate) =>
        $"{translate("CHECK_THROTTLE")}: {(throttle * 100).ToString("0", CultureInfo.InvariantCulture)} %";

    public static string ChannelKey(StickFunction channel) => "CHECK_" + channel.ToString().ToUpperInvariant();

    static string Upper<T>(T value) where T : Enum => value.ToString().ToUpperInvariant();

    static CheckDirection Classify(Vec3 v) => Math.Abs(v.Z) >= Math.Abs(v.Y)
        ? (Vec3.Dot(v, BodyAxes.Up) >= 0 ? CheckDirection.Up : CheckDirection.Down)
        : (Vec3.Dot(v, BodyAxes.Right) >= 0 ? CheckDirection.Right : CheckDirection.Left);

    /// <summary>Pilot rates in body axes: roll right = −M_x, pitch up = +M_y, yaw right = −M_z (see <see cref="BodyAxes"/>).</summary>
    static CheckEffect Effect(Vec3 m)
    {
        double roll = -m.X, pitch = m.Y, yaw = -m.Z;
        double largest = Math.Max(Math.Abs(roll), Math.Max(Math.Abs(pitch), Math.Abs(yaw)));
        if (largest < 1e-12) return CheckEffect.None;
        if (largest == Math.Abs(roll)) return roll > 0 ? CheckEffect.RollRight : CheckEffect.RollLeft;
        if (largest == Math.Abs(pitch)) return pitch > 0 ? CheckEffect.PitchUp : CheckEffect.PitchDown;
        return yaw > 0 ? CheckEffect.YawRight : CheckEffect.YawLeft;
    }
}
