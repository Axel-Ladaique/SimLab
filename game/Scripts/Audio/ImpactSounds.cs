using System.Collections.Generic;
using Godot;

namespace SimLab.Game.Audio;

/// <summary>Loads the impact one-shot library, shared by <see cref="AircraftAudio"/> (in-flight impacts) and the
/// sound screen's preview.</summary>
public static class ImpactSounds
{
    public static List<AudioStream> Load(string directory)
    {
        var streams = new List<AudioStream>();
        foreach (var name in FileNames(directory)) streams.Add(GD.Load<AudioStream>($"{directory}/{name}"));
        return streams;
    }

    /// <summary>Resolves the .ogg resources in a directory, also accepting the ".ogg.remap"/".ogg.import" names an
    /// exported build renames them to (the resource loader still resolves the plain ".ogg" res:// path).</summary>
    static IEnumerable<string> FileNames(string directory)
    {
        var seen = new HashSet<string>();
        foreach (var file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") || file.EndsWith(".import") ? file[..file.LastIndexOf('.')] : file;
            if (name.EndsWith(".ogg") && seen.Add(name)) yield return name;
        }
    }
}
