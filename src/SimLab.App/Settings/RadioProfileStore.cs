using System.Text.Json;
using SimLab.Input;

namespace SimLab.App.Settings;

/// <summary>One JSON profile per radio, named after its device GUID.</summary>
public sealed class RadioProfileStore
{
    readonly string _directory;

    public RadioProfileStore(string directory) => _directory = directory;

    public string PathFor(string guid)
    {
        var safe = new string(guid.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
        return Path.Combine(_directory, (safe.Length == 0 ? "unknown" : safe) + ".json");
    }

    public RadioProfile? Load(string guid, out string? error)
    {
        error = null;
        var path = PathFor(guid);
        if (!File.Exists(path)) return null;
        try
        {
            return RadioProfile.FromJson(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"{path}: {ex.Message}";
            return null;
        }
    }

    public void Save(RadioProfile profile)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(profile.DeviceGuid), profile.ToJson());
    }
}
