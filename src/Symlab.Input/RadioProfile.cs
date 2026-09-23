using System.Text.Json;
using System.Text.Json.Serialization;

namespace Symlab.Input;

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
    public List<SwitchBinding> Switches { get; set; } = [];

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

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Parses and validates a profile; malformed or inconsistent data throws <see cref="InvalidDataException"/>.</summary>
    public static RadioProfile FromJson(string json)
    {
        RadioProfile profile;
        try
        {
            profile = JsonSerializer.Deserialize<RadioProfile>(json, Options) ?? throw new InvalidDataException("Empty radio profile.");
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
        return profile;
    }
}
