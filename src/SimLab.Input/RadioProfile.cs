using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SimLab.Input;

/// <summary>Calibration and channel assignment for one physical radio, persisted as JSON per device GUID.</summary>
public sealed class RadioProfile
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string DeviceGuid { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public Dictionary<StickFunction, ChannelSettings> Channels { get; set; } = new();
    public List<SwitchAssignment> Switches { get; set; } = [];

    public StickState Read(RawInputFrame frame)
    {
        double Get(StickFunction function)
        {
            if (!Channels.TryGetValue(function, out var c) || c.AxisIndex < 0 || c.AxisIndex >= frame.Axes.Length)
                return function == StickFunction.Throttle ? -1 : 0;
            return ChannelPipeline.Process(frame.Axes[c.AxisIndex], c);
        }

        return new StickState(
            (Get(StickFunction.Throttle) + 1) / 2,
            Get(StickFunction.Aileron),
            Get(StickFunction.Elevator),
            Get(StickFunction.Rudder));
    }

    /// <summary>Sets the channel's direction, keeping its other settings; false if the function has no channel.</summary>
    public bool SetReversed(StickFunction function, bool reversed)
    {
        if (!Channels.TryGetValue(function, out var c)) return false;
        Channels[function] = c with { Reversed = reversed };
        return true;
    }

    /// <summary>Assigns a switch to its function, replacing any earlier assignment of that function.</summary>
    public void SetSwitch(SwitchAssignment assignment)
    {
        ClearSwitch(assignment.Function);
        Switches.Add(assignment);
    }

    public void ClearSwitch(SwitchFunction function) => Switches.RemoveAll(s => s.Function == function);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Parses and validates a profile; malformed or inconsistent data throws <see cref="InvalidDataException"/>.</summary>
    public static RadioProfile FromJson(string json)
    {
        RadioProfile profile;
        try
        {
            var node = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Empty radio profile.");
            if (node["Switches"] is JsonArray switches && LegacySwitches.IsLegacy(switches))
                node["Switches"] = JsonSerializer.SerializeToNode(LegacySwitches.Convert(switches, Options), Options);
            profile = node.Deserialize<RadioProfile>(Options) ?? throw new InvalidDataException("Empty radio profile.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid radio profile: {ex.Message}", ex);
        }
        if (profile.Channels is null) throw new InvalidDataException("Radio profile has no Channels.");
        if (profile.Switches is null) throw new InvalidDataException("Radio profile has no Switches.");
        foreach (var (function, c) in profile.Channels)
        {
            if (c is null) throw new InvalidDataException($"Channel {function} has no settings.");
            if (c.AxisIndex < 0) throw new InvalidDataException($"Channel {function}: AxisIndex must be 0 or more.");
            if (c.Calibration is null) throw new InvalidDataException($"Channel {function}: Calibration is missing.");
            var cal = c.Calibration;
            if (!(cal.Min <= cal.Center && cal.Center <= cal.Max))
                throw new InvalidDataException($"Channel {function}: calibration must satisfy Min <= Center <= Max.");
        }
        var assigned = new HashSet<SwitchFunction>();
        foreach (var s in profile.Switches)
        {
            if (s is null) throw new InvalidDataException("Empty switch assignment.");
            if (!Enum.IsDefined(s.Function)) throw new InvalidDataException($"Unknown switch function {(int)s.Function}.");
            if (!assigned.Add(s.Function)) throw new InvalidDataException($"Switch {s.Function} is assigned twice.");
            if ((s.Source.AxisIndex is null) == (s.Source.ButtonIndex is null))
                throw new InvalidDataException($"Switch {s.Function} needs exactly one axis or one button.");
            if (s.Source.AxisIndex < 0 || s.Source.ButtonIndex < 0) throw new InvalidDataException($"Switch {s.Function}: negative index.");
            if (s.Positions is null || s.Positions.Count is < 2 or > 3)
                throw new InvalidDataException($"Switch {s.Function} needs 2 or 3 positions.");
            foreach (var p in s.Positions)
            {
                if (p is null) throw new InvalidDataException($"Switch {s.Function} has an empty position.");
                if (p.State is int state && (state < 0 || state >= SwitchStates.Count(s.Function)))
                    throw new InvalidDataException($"Switch {s.Function}: unknown state {state}.");
            }
        }
        return profile;
    }
}
