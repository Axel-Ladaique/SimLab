using Godot;

namespace SimLab.Game.Audio;

/// <summary>Looping, non-positional field ambience on the Ambience bus.</summary>
public partial class FieldAmbience : Node
{
    const string Preferred = "res://Audio/ambience/birds-and-wind-ambient.ogg";
    const string Fallback = "res://Audio/ambience/birds-isaiah658.ogg";

    AudioStreamPlayer? _player;

    public override void _Ready()
    {
        // Under `--headless`, Godot forces its dummy audio driver, which never runs the mix thread; a playback
        // started there can't be cleanly finalized later (even by freeing the node before Quit()), and is
        // reported as a leaked resource at process exit. Nothing would be heard anyway, so skip it.
        if (AudioBuses.Headless) return;
        var path = ResourceLoader.Exists(Preferred) ? Preferred : Fallback;
        if (!ResourceLoader.Exists(path)) return;
        var stream = GD.Load<AudioStreamOggVorbis>(path);
        stream.Loop = true;
        _player = new AudioStreamPlayer { Stream = stream, Bus = AudioBuses.Ambience };
        AddChild(_player);
        _player.Play();
    }

    // A looping Ogg Vorbis playback never reaches its natural end, so it must be stopped explicitly before the
    // tree tears down; otherwise Godot reports its internal playback objects as leaked at process exit.
    public override void _ExitTree()
    {
        if (_player is null) return;
        _player.Stop();
        _player.Stream = null;
    }
}
