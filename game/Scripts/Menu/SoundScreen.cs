using System.Linq;
using Godot;
using SimLab.App.Audio;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.Flight.Airframe;
using SimLab.Game.Audio;

namespace SimLab.Game.Menu;

/// <summary>Eight per-sound volume sliders (bus levels plus the four synthesized voices and the impacts one-shots)
/// and a "Listen" toggle that plays <see cref="SoundPreview"/>'s scripted loop for the last selected aircraft, so
/// the mix can be tuned without flying.</summary>
public partial class SoundScreen : Control
{
    const int SampleRate = 44100;
    // Godot rounds the generator's ring buffer up to a power of two frames: 0.04 s at 44.1 kHz gives 2048 frames
    // (~46 ms of queued audio, still above one 30 fps frame); 0.05 s would round up to 4096 (~93 ms).
    const float BufferSeconds = 0.04f;

    Services _services = null!;
    readonly EngineSynth _synth = new(SampleRate);
    readonly GeneratorFeeder _feeder;
    readonly RandomNumberGenerator _rng = new();
    AudioStreamPlayer _voice = null!;
    AudioStreamGeneratorPlayback? _playback;
    System.Collections.Generic.List<AudioStream> _impacts = [];
    Button _listenButton = null!;
    Label _error = null!;
    EngineSoundModel? _engine;
    SoundSpec _spec = SoundSpec.Default;
    bool _listening;
    double _previewTime;
    AudioStreamPlayer _impactPlayer = null!;

    public SoundScreen() => _feeder = new GeneratorFeeder(_synth);

    public void Init(Services services, System.Action back)
    {
        _services = services;
        var column = Ui.Screen(this, Ui.T("SND_TITLE"));

        void Change(System.Func<AudioSettings, AudioSettings> update)
        {
            _services.Settings = _services.Settings with { Audio = update(_services.Settings.Audio).Sanitized() };
            _services.SaveSettings();
            AudioBuses.Apply(_services.Settings);
        }

        HBoxContainer VolumeSlider(string key, System.Func<AudioSettings, double> get, System.Func<AudioSettings, double, AudioSettings> set) =>
            Ui.Slider(Ui.T(key), 0, 1, 0.05, get(_services.Settings.Audio), v => Change(a => set(a, v)), "0.00");

        column.AddChild(VolumeSlider("SND_MASTER", a => a.Master, (a, v) => a with { Master = v }));
        column.AddChild(VolumeSlider("SND_AIRCRAFT", a => a.Aircraft, (a, v) => a with { Aircraft = v }));
        column.AddChild(VolumeSlider("SND_PROPELLER", a => a.Propeller, (a, v) => a with { Propeller = v }));
        column.AddChild(VolumeSlider("SND_MOTOR", a => a.Motor, (a, v) => a with { Motor = v }));
        column.AddChild(VolumeSlider("SND_WIND", a => a.Wind, (a, v) => a with { Wind = v }));
        column.AddChild(VolumeSlider("SND_ROLLING", a => a.Rolling, (a, v) => a with { Rolling = v }));
        column.AddChild(VolumeSlider("SND_IMPACTS", a => a.Impacts, (a, v) => a with { Impacts = v }));
        column.AddChild(VolumeSlider("SND_AMBIENCE", a => a.Ambience, (a, v) => a with { Ambience = v }));

        _listenButton = Ui.Button(Ui.T("SND_LISTEN"), ToggleListen);
        column.AddChild(_listenButton);
        _error = Ui.Text("", 14);
        column.AddChild(_error);
        column.AddChild(Ui.Button(Ui.T("BACK"), () =>
        {
            StopListening();
            back();
        }));

        AddChild(new FieldAmbience());

        _voice = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = BufferSeconds },
            Bus = AudioBuses.Aircraft,
        };
        AddChild(_voice);

        // One persistent player, reused for every impact one-shot (like AircraftAudio's own impact players):
        // never freed until _ExitTree, so nothing ever holds a reference to a node Godot has already destroyed.
        _impactPlayer = new AudioStreamPlayer { Bus = AudioBuses.Aircraft };
        AddChild(_impactPlayer);

        _impacts = ImpactSounds.Load("res://Audio/impacts");
        LoadAircraft();
    }

    void LoadAircraft()
    {
        var catalog = AircraftCatalog.List(AppPaths.AircraftRoot, out _);
        string? id = catalog.Any(a => a.Id == _services.Settings.LastAircraft) ? _services.Settings.LastAircraft
            : catalog.Count > 0 ? catalog[0].Id : null;
        if (id is null)
        {
            _error.Text = Ui.T("SND_NO_AIRCRAFT");
            _listenButton.Disabled = true;
            return;
        }
        try
        {
            var folder = System.IO.Path.Combine(AppPaths.AircraftRoot, id);
            var definition = AircraftLoader.Load(folder);
            _spec = SoundSpecLoader.Load(folder);
            if (definition.Power is { } power)
            {
                _engine = EngineSoundModel.For(power, _spec);
            }
            else
            {
                _error.Text = string.Format(Ui.T("SND_NO_POWER"), id);
                _listenButton.Disabled = true;
            }
        }
        catch (System.Exception ex) when (ex is System.IO.InvalidDataException or System.IO.FileNotFoundException
            or System.ArgumentException or System.Text.Json.JsonException)
        {
            _error.Text = $"{id}: {ex.Message}";
            _listenButton.Disabled = true;
        }
    }

    void ToggleListen()
    {
        if (_listening) StopListening();
        else StartListening();
    }

    void StartListening()
    {
        if (_engine is null || AudioBuses.Headless) return;
        // A flight left while paused leaves the Aircraft bus muted; the preview would otherwise be silent.
        AudioBuses.SetAircraftMuted(false);
        _listening = true;
        _previewTime = 0;
        _synth.Reset();
        _voice.Play();
        _playback = (AudioStreamGeneratorPlayback)_voice.GetStreamPlayback();
        _listenButton.Text = Ui.T("SND_STOP");
    }

    void StopListening()
    {
        if (!_listening) return;
        _listening = false;
        _voice.Stop();
        _playback?.Dispose();
        _playback = null;
        _impactPlayer.Stop();
        _listenButton.Text = Ui.T("SND_LISTEN");
    }

    public override void _Process(double delta)
    {
        if (!_listening || _engine is null) return;
        _synth.Mix = VoiceMix.From(_services.Settings.Audio);
        double previous = _previewTime;
        _previewTime += delta;
        // Crossing detection over the elapsed span, not a sample inside a fixed window: sampling a boolean at a
        // point can miss the impact entirely when a slow frame (or a low frame rate) jumps clean over the window.
        if (SoundPreview.ImpactBetween(previous, _previewTime)) PlayImpact();
        _feeder.Push(_playback, SoundPreview.At(_previewTime, _engine, _spec));
    }

    void PlayImpact()
    {
        if (_impacts.Count == 0) return;
        double volume = ImpactMix.Linear(1.0, _services.Settings.Audio.Impacts);
        if (volume <= 0.001) return;
        _impactPlayer.Stream = _impacts[_rng.RandiRange(0, _impacts.Count - 1)];
        _impactPlayer.VolumeDb = Mathf.LinearToDb((float)volume);
        _impactPlayer.Play();
    }

    // The generated voice never reaches a natural end, so it must be stopped explicitly before the tree tears
    // down; otherwise Godot reports its playback object as leaked at process exit. The impact player is a plain
    // one-shot (like AircraftAudio's own impact players) and doesn't need this, but stopping it too is harmless.
    public override void _ExitTree()
    {
        _voice.Stop();
        _playback?.Dispose();
        _playback = null;
        _impactPlayer.Stop();
    }
}
