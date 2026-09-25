using System.Text.Json;
using System.Text.Json.Serialization;
using SimLab.App.Field;

namespace SimLab.App.Settings;

public enum StickMode { Mode1 = 1, Mode2 = 2 }

public sealed record AppSettings
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public double FovDeg { get; init; } = 50;
    public bool AutoZoom { get; init; } = true;
    public bool ShowFlightData { get; init; }
    public bool RecordFlights { get; init; } = true;
    public bool VSync { get; init; } = true;
    public bool Fullscreen { get; init; } = true;
    public string Language { get; init; } = "fr";
    public StickMode StickMode { get; init; } = StickMode.Mode2;
    public string LastAircraft { get; init; } = "trainer";
    public string LastField { get; init; } = "club";
    public FlightConditions Conditions { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings()).Sanitized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public AppSettings Sanitized() => this with
    {
        FovDeg = Math.Clamp(FovDeg, 10, 100),
        Language = Language is "fr" or "en" ? Language : "fr",
        Conditions = Conditions ?? new FlightConditions(),
        Audio = (Audio ?? new AudioSettings()).Sanitized(),
        LastField = FieldCatalog.Find(LastField ?? "").Id,
    };
}
