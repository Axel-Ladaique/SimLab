using System.Collections.Generic;
using Godot;
using SimLab.App.Audio;
using SimLab.App.Session;

namespace SimLab.Game.Audio;

/// <summary>Aircraft voice at the aircraft position: synthesized motor/propeller/wind/rolling through an
/// AudioStreamGenerator, an optional recorded motor loop, and impact one-shots. Heard from the current camera.</summary>
public partial class AircraftAudio : Node3D
{
    const int SampleRate = 44100;
    const float BufferSeconds = 0.05f;
    const float UnitSize = 12f;
    const float MaxDistance = 600f;
    // Rendered and pushed to the generator in fixed-size chunks so steady-state _Process does no allocation.
    const int ChunkFrames = 256;

    FlightSession _session = null!;
    AircraftSound _sound = null!;
    readonly EngineSynth _synth = new(SampleRate);
    AudioStreamPlayer3D _voice = null!;
    AudioStreamGeneratorPlayback? _playback;
    AudioStreamPlayer3D? _sample;
    double _sampleRpm;
    readonly List<AudioStream> _impacts = [];
    readonly List<AudioStreamPlayer3D> _impactPlayers = [];
    int _nextImpact;
    readonly RandomNumberGenerator _rng = new();
    readonly float[] _chunkScratch = new float[ChunkFrames];
    readonly Vector2[] _chunkStereo = new Vector2[ChunkFrames];

    /// <summary>Builds the node tree; playback only starts in <see cref="_Ready"/>, once this subtree is actually
    /// inside the scene tree (the caller adds this node to a still-detached parent before this returns).</summary>
    public void Init(FlightSession session, SoundSpec spec)
    {
        _session = session;
        _sound = new AircraftSound(session, spec);

        _voice = Player(new AudioStreamGenerator { MixRate = SampleRate, BufferLength = BufferSeconds });

        if (spec.SamplePath is { } path && spec.SampleRpm is { } rpm && AudioStreamOggVorbis.LoadFromFile(path) is { } ogg)
        {
            ogg.Loop = true;
            _sample = Player(ogg);
            _sampleRpm = rpm;
            _synth.EngineMuted = true;
        }

        foreach (var name in ImpactFileNames("res://Audio/impacts"))
            _impacts.Add(GD.Load<AudioStream>($"res://Audio/impacts/{name}"));
        for (int i = 0; i < 4; i++) _impactPlayers.Add(Player(null));
    }

    public override void _Ready()
    {
        // Under `--headless`, Godot forces its dummy audio driver, which never runs the mix thread; a playback
        // started there can't be cleanly finalized later (even by freeing the node before Quit()), and is
        // reported as a leaked resource at process exit. Nothing would be heard anyway, so skip it.
        if (AudioBuses.Headless) return;
        _voice.Play();
        _playback = (AudioStreamGeneratorPlayback)_voice.GetStreamPlayback();
        _sample?.Play();
    }

    // The generated voice never reaches a natural end, and a looping sample doesn't either, so both must be
    // stopped explicitly before the tree tears down; otherwise Godot reports their playback objects as leaked
    // at process exit.
    public override void _ExitTree()
    {
        _voice.Stop();
        _sample?.Stop();
        _playback?.Dispose();
        _playback = null;
    }

    /// <summary>Resolves the .ogg resources in a directory, also accepting the ".ogg.remap"/".ogg.import" names an
    /// exported build renames them to (the resource loader still resolves the plain ".ogg" res:// path).</summary>
    static IEnumerable<string> ImpactFileNames(string directory)
    {
        var seen = new HashSet<string>();
        foreach (var file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") || file.EndsWith(".import") ? file[..file.LastIndexOf('.')] : file;
            if (name.EndsWith(".ogg") && seen.Add(name)) yield return name;
        }
    }

    AudioStreamPlayer3D Player(AudioStream? stream)
    {
        var p = new AudioStreamPlayer3D
        {
            Stream = stream,
            Bus = AudioBuses.Aircraft,
            UnitSize = UnitSize,
            MaxDistance = MaxDistance,
            // The aircraft visual (and this node, its child) is moved in _Process, not the physics step, so
            // PhysicsStep would sample a stale transform at render rates above the physics rate, undershooting
            // and jittering the Doppler velocity estimate.
            DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.IdleStep,
            AttenuationFilterCutoffHz = 8000,
        };
        AddChild(p);
        return p;
    }

    public override void _Process(double delta)
    {
        AudioBuses.SetAircraftMuted(_session.Paused);
        if (_session.Paused) return;
        var frame = _sound.Update(delta);
        if (frame.Reset)
        {
            _synth.Reset();
            _playback?.ClearBuffer(); // drop whatever pre-reset audio was already queued
        }

        // Null under a driver with no real audio output (headless smoke tests use the dummy driver).
        // Rendered in fixed-size chunks (see ChunkFrames) so no array is allocated per frame; EngineSynth ramps
        // from its last sample to the target SynthParams on every Render call, so pushing the same target across
        // several chunks in one _Process is correct (the parameters just haven't changed since the last chunk).
        while (_playback is not null && _playback.GetFramesAvailable() >= ChunkFrames)
        {
            _synth.Render(_chunkScratch, frame.Synth);
            for (int i = 0; i < ChunkFrames; i++) _chunkStereo[i] = new Vector2(_chunkScratch[i], _chunkScratch[i]);
            _playback.PushBuffer(_chunkStereo);
        }

        if (_sample is not null)
        {
            _sample.PitchScale = (float)Mathf.Clamp(frame.Rpm / _sampleRpm, 0.05, 4.0);
            _sample.VolumeDb = frame.Rpm < 50 ? -80f : Mathf.LinearToDb((float)Mathf.Clamp(frame.Synth.PropGain + 0.2, 0, 1));
        }

        foreach (var impact in frame.Impacts) PlayImpact(impact);
    }

    void PlayImpact(ImpactEvent impact)
    {
        if (_impacts.Count == 0 || AudioBuses.Headless) return;
        var player = _impactPlayers[_nextImpact++ % _impactPlayers.Count];
        player.Stream = _impacts[_rng.RandiRange(0, _impacts.Count - 1)];
        player.VolumeDb = Mathf.LinearToDb((float)Mathf.Clamp(0.25 + 0.75 * impact.Intensity, 0, 1));
        player.PitchScale = impact.Kind == ImpactKind.Crash ? 0.8f : 1f + _rng.RandfRange(-0.08f, 0.08f);
        player.Play();
    }
}
