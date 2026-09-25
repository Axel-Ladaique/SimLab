using Godot;
using SimLab.App.Settings;

namespace SimLab.Game.Audio;

/// <summary>Master → Aircraft, Ambience buses, created at run time so no bus layout resource is needed.</summary>
public static class AudioBuses
{
    public const string Aircraft = "Aircraft";
    public const string Ambience = "Ambience";

    /// <summary>True under `--headless`, which forces Godot's dummy audio driver: starting playback there is
    /// pointless (nothing is heard) and, at least on 4.7, leaves Ogg Vorbis playback objects the engine reports
    /// as leaked resources at process exit because the driver never runs the mix thread that would finalize a
    /// stopped stream. Godot glue nodes skip `Play()` in that case.</summary>
    static readonly string[] Names = [Aircraft, Ambience];

    public static bool Headless => DisplayServer.GetName() == "headless";

    public static void Ensure()
    {
        foreach (var name in Names)
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus();
            int index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
    }

    public static void Apply(AppSettings s)
    {
        Ensure();
        Set("Master", s.Audio.Master);
        Set(Aircraft, s.Audio.Aircraft);
        Set(Ambience, s.Audio.Ambience);
    }

    public static void SetAircraftMuted(bool muted)
    {
        Ensure();
        AudioServer.SetBusMute(AudioServer.GetBusIndex(Aircraft), muted);
    }

    static void Set(string bus, double linear)
    {
        int index = AudioServer.GetBusIndex(bus);
        AudioServer.SetBusVolumeDb(index, linear <= 0.001 ? -80f : Mathf.LinearToDb((float)linear));
    }
}
