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

        int blades = Int(sound, "blades", path) ?? SoundSpec.Default.Blades;
        int polePairs = Int(sound, "polePairs", path) ?? SoundSpec.Default.PolePairs;
        string? sample = null;
        if (TryGet(sound, "sample", out var s))
            sample = s.ValueKind == JsonValueKind.String ? s.GetString() : throw Invalid(path, "sound.sample must be a file name string.");
        double? sampleRpm = null;
        if (TryGet(sound, "sampleRpm", out var r))
            sampleRpm = r.ValueKind == JsonValueKind.Number ? r.GetDouble() : throw Invalid(path, "sound.sampleRpm must be a number.");

        if (blades is < 1 or > 16) throw Invalid(path, "sound.blades must be between 1 and 16.");
        if (polePairs is < 1 or > 20) throw Invalid(path, "sound.polePairs must be between 1 and 20.");
        if ((sample is null) != (sampleRpm is null)) throw Invalid(path, "sound.sample and sound.sampleRpm must be given together.");
        if (sampleRpm is <= 0) throw Invalid(path, "sound.sampleRpm must be positive.");
        string? samplePath = sample is null ? null : Path.Combine(Path.GetDirectoryName(path)!, sample);
        if (samplePath is not null && !File.Exists(samplePath)) throw Invalid(path, $"sound.sample file not found: {sample}.");
        return new SoundSpec(blades, polePairs, samplePath, sampleRpm);
    }

    /// <summary>An optional whole-number field; a string, fraction or out-of-range value is rejected with the file and field.</summary>
    static int? Int(JsonElement sound, string name, string path)
    {
        if (!TryGet(sound, name, out var v)) return null;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int value))
            throw Invalid(path, $"sound.{name} must be a whole number.");
        return value;
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
