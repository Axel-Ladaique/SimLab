namespace SimLab.Input;

/// <summary>
/// What a radio switch can drive. <see cref="Gear"/>, <see cref="Flaps"/> and <see cref="ThrottleCut"/> follow the
/// switch position (held); the others act when the switch enters a position.
/// </summary>
public enum SwitchFunction { Gear, Flaps, ThrottleCut, Camera, Osd, Wind, Pause, Reset }

/// <summary>A physical switch: one axis or one button of the radio.</summary>
public readonly record struct SwitchSource(int? AxisIndex = null, int? ButtonIndex = null)
{
    /// <summary>The raw value: the axis value, or −1 / +1 for a released / pressed button; null when out of range.</summary>
    public double? Read(RawInputFrame frame)
    {
        if (ButtonIndex is int b) return b >= 0 && b < frame.Buttons.Length ? (frame.Buttons[b] ? 1 : -1) : null;
        if (AxisIndex is int a) return a >= 0 && a < frame.Axes.Length ? frame.Axes[a] : null;
        return null;
    }
}

/// <summary>One learned switch position: the raw value it rests at and the state it selects (null: no effect).</summary>
public sealed record SwitchPosition(double Value, int? State);

/// <summary>A function bound to a physical switch, position by position (2 or 3 positions).</summary>
public sealed record SwitchAssignment(SwitchFunction Function, SwitchSource Source, List<SwitchPosition> Positions);

/// <summary>The states each <see cref="SwitchFunction"/> can be put in, as indices, with their translation keys.</summary>
public static class SwitchStates
{
    public const int GearDown = 0, GearUp = 1;
    public const int FlapsUp = 0, FlapsTakeoff = 1, FlapsLanding = 2;
    public const int ThrottleArmed = 0, ThrottleCut = 1;
    public const int CameraGround = 0, CameraFpv = 1, CameraChase = 2;
    public const int OsdOn = 0, OsdOff = 1;
    public const int WindOn = 0, WindOff = 1;
    public const int PauseRunning = 0, PausePaused = 1;
    public const int ResetFire = 0;

    public static readonly SwitchFunction[] All = Enum.GetValues<SwitchFunction>();

    /// <summary>State names in index order (the last part of their translation keys).</summary>
    public static IReadOnlyList<string> Names(SwitchFunction function) => function switch
    {
        SwitchFunction.Gear => ["DOWN", "UP"],
        SwitchFunction.Flaps => ["UP", "TAKEOFF", "LANDING"],
        SwitchFunction.ThrottleCut => ["ARMED", "CUT"],
        SwitchFunction.Camera => ["GROUND", "FPV", "CHASE"],
        SwitchFunction.Osd => ["ON", "OFF"],
        SwitchFunction.Wind => ["ON", "OFF"],
        SwitchFunction.Pause => ["RUNNING", "PAUSED"],
        SwitchFunction.Reset => ["RESET"],
        _ => throw new ArgumentOutOfRangeException(nameof(function)),
    };

    public static int Count(SwitchFunction function) => Names(function).Count;

    public static bool IsHeld(SwitchFunction function) =>
        function is SwitchFunction.Gear or SwitchFunction.Flaps or SwitchFunction.ThrottleCut;

    public static string FunctionKey(SwitchFunction function) => "SWITCH_" + Upper(function);

    public static string StateKey(SwitchFunction function, int state) => $"SWITCH_{Upper(function)}_{Names(function)[state]}";

    static string Upper(SwitchFunction function) =>
        function == SwitchFunction.ThrottleCut ? "THROTTLE_CUT" : function.ToString().ToUpperInvariant();

    /// <summary>Suggested states for a freshly learned switch, lowest raw value first (null: no effect).</summary>
    public static int?[] Defaults(SwitchFunction function, int positions)
    {
        if (function == SwitchFunction.Reset) return positions == 3 ? [null, null, ResetFire] : [null, ResetFire];
        if (function == SwitchFunction.Camera && positions == 2) return [CameraGround, CameraFpv];
        bool three = Count(function) == 3;
        if (positions == 3) return three ? [0, 1, 2] : [0, null, 1];
        return three ? [0, 2] : [0, 1];
    }

    /// <summary>The state after <paramref name="state"/> when the pilot clicks a position: the next state, "no
    /// effect" (null) after the last one, the first state after "no effect".</summary>
    public static int? Next(SwitchFunction function, int? state) =>
        state is not int s ? 0 : s + 1 < Count(function) ? s + 1 : null;
}
