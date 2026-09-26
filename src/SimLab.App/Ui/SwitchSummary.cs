using SimLab.App.Session;
using SimLab.Flight.Controls;
using SimLab.Input;

namespace SimLab.App.Ui;

/// <summary>Translation keys the radio screen shows for switches, axes and the switch-driven controls.</summary>
public static class SwitchSummary
{
    public static string SourceKey(SwitchSource source) => source.ButtonIndex is not null ? "RADIO_SOURCE_BUTTON" : "RADIO_SOURCE_AXIS";

    /// <summary>One-based axis or button number, as radios and the raw-axes list count them.</summary>
    public static int SourceNumber(SwitchSource source) => (source.ButtonIndex ?? source.AxisIndex ?? 0) + 1;

    /// <summary>What a raw axis drives: stick channels (STICK_*) then switch functions (SWITCH_*).</summary>
    public static IReadOnlyList<string> DrivenBy(RadioProfile profile, int axis)
    {
        var keys = new List<string>();
        foreach (var (function, channel) in profile.Channels)
            if (channel.AxisIndex == axis) keys.Add("STICK_" + function.ToString().ToUpperInvariant());
        foreach (var s in profile.Switches)
            if (s.Source.AxisIndex == axis) keys.Add(SwitchStates.FunctionKey(s.Function));
        return keys;
    }

    /// <summary>Gear, flaps and throttle-cut state keys for the control-check status line.</summary>
    public static IReadOnlyList<string> Status(in ControlInputs controls) =>
    [
        SwitchStates.StateKey(SwitchFunction.Gear, controls.GearUp ? SwitchStates.GearUp : SwitchStates.GearDown),
        SwitchStates.StateKey(SwitchFunction.Flaps, controls.Flap >= FlapSetting.Landing ? SwitchStates.FlapsLanding
            : controls.Flap >= FlapSetting.Half ? SwitchStates.FlapsTakeoff : SwitchStates.FlapsUp),
        SwitchStates.StateKey(SwitchFunction.ThrottleCut, controls.ThrottleCut ? SwitchStates.ThrottleCut : SwitchStates.ThrottleArmed),
    ];
}
