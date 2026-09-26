using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimLab.Input;

/// <summary>
/// Reads the switch bindings saved before 2026-09-26 (one action per switch, "on" above a threshold) as positional
/// assignments. The old "next camera" switch has no positional equivalent and is dropped.
/// </summary>
static class LegacySwitches
{
    internal enum LegacyAction { Reset, Pause, ToggleWind, NextCamera, GearUp, Flaps }

    internal sealed record LegacyBinding(LegacyAction Action, int? ButtonIndex = null, int? AxisIndex = null, double Threshold = 0.5);

    public static bool IsLegacy(JsonArray switches) => switches.Any(s => s is JsonObject o && o.ContainsKey("Action"));

    public static List<SwitchAssignment> Convert(JsonArray switches, JsonSerializerOptions options)
    {
        var result = new List<SwitchAssignment>();
        foreach (var b in switches.Deserialize<List<LegacyBinding>>(options) ?? [])
        {
            var source = new SwitchSource(b.AxisIndex, b.ButtonIndex);
            bool button = b.ButtonIndex is not null;
            double low = button ? -1 : b.Threshold - 0.5;
            double high = button ? 1 : b.Threshold + 0.5;
            List<SwitchPosition> LowHigh(int? lowState, int? highState) => [new(low, lowState), new(high, highState)];
            SwitchAssignment? converted = b.Action switch
            {
                LegacyAction.GearUp => new(SwitchFunction.Gear, source, LowHigh(SwitchStates.GearDown, SwitchStates.GearUp)),
                LegacyAction.Flaps => new(SwitchFunction.Flaps, source, button
                    ? [new(-1, SwitchStates.FlapsUp), new(1, SwitchStates.FlapsLanding)]
                    : [new(-1, SwitchStates.FlapsUp), new(0, SwitchStates.FlapsTakeoff), new(1, SwitchStates.FlapsLanding)]),
                LegacyAction.Pause => new(SwitchFunction.Pause, source, LowHigh(SwitchStates.PauseRunning, SwitchStates.PausePaused)),
                LegacyAction.ToggleWind => new(SwitchFunction.Wind, source, LowHigh(SwitchStates.WindOff, SwitchStates.WindOn)),
                LegacyAction.Reset => new(SwitchFunction.Reset, source, LowHigh(null, SwitchStates.ResetFire)),
                _ => null,
            };
            if (converted is not null) result.Add(converted);
        }
        return result;
    }
}
