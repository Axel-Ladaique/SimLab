using System.Text.Json;

namespace SimLab.App.Audio;

/// <summary>Sound data for an aircraft's power plant. SamplePath is absolute; when set, the engine voice plays that
/// loop pitched by Rpm / SampleRpm instead of the synthesized motor and propeller.</summary>
public sealed record SoundSpec(int Blades = 2, int PolePairs = 7, string? SamplePath = null, double? SampleRpm = null)
{
    public static readonly SoundSpec Default = new();
}

/// <summary>Reads the optional <c>sound</c> block of the power.json named by an aircraft folder's aircraft.json.</summary>
public static class SoundSpecLoader
{
    static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static SoundSpec Load(string aircraftFolder)
    {
        using var aircraft = JsonDocument.Parse(File.ReadAllText(Path.Combine(aircraftFolder, "aircraft.json")), Options);
        if (!TryGet(aircraft.RootElement, "power", out var powerName) || powerName.ValueKind != JsonValueKind.String)
            return SoundSpec.Default;
        var path = Path.Combine(aircraftFolder, powerName.GetString()!);
        if (!File.Exists(path)) return SoundSpec.Default;

        using var power = JsonDocument.Parse(File.ReadAllText(path), Options);
        if (!TryGet(power.RootElement, "sound", out var sound)) return SoundSpec.Default;

        int blades = TryGet(sound, "blades", out var b) ? b.GetInt32() : SoundSpec.Default.Blades;
        int polePairs = TryGet(sound, "polePairs", out var pp) ? pp.GetInt32() : SoundSpec.Default.PolePairs;
        string? sample = TryGet(sound, "sample", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        double? sampleRpm = TryGet(sound, "sampleRpm", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : null;

        if (blades is < 1 or > 6) throw Invalid(path, "sound.blades must be between 1 and 6.");
        if (polePairs is < 1 or > 20) throw Invalid(path, "sound.polePairs must be between 1 and 20.");
        if ((sample is null) != (sampleRpm is null)) throw Invalid(path, "sound.sample and sound.sampleRpm must be given together.");
        if (sampleRpm is <= 0) throw Invalid(path, "sound.sampleRpm must be positive.");
        string? samplePath = sample is null ? null : Path.Combine(Path.GetDirectoryName(path)!, sample);
        if (samplePath is not null && !File.Exists(samplePath)) throw Invalid(path, $"sound.sample file not found: {sample}.");
        return new SoundSpec(blades, polePairs, samplePath, sampleRpm);
    }

    static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        value = default;
        if (e.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        return false;
    }

    static InvalidDataException Invalid(string path, string message) => new($"{path}: {message}");
}
