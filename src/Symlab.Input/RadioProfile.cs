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
            if (!Channels.TryGetValue(function, out var c) || c.AxisIndex >= frame.Axes.Length)
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

    public static RadioProfile FromJson(string json) =>
        JsonSerializer.Deserialize<RadioProfile>(json, Options) ?? throw new InvalidDataException("Empty radio profile.");
}
