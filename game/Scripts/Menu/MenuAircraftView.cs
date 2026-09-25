using Godot;
using SimLab.App.Audio;
using SimLab.App.Settings;
using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Game.Audio;
using SimLab.Game.Radio;

namespace SimLab.Game.Menu;

/// <summary>
/// The main menu's live view: the selected aircraft in flight against the sky, seen from 3/4 front by a fixed camera.
/// Its surfaces follow the radio or keyboard through the aircraft's own mixing and servos (like the radio screen's
/// <see cref="ControlPreview"/>), it banks, pitches, yaws and creeps forward within small limits in the direction its
/// control moments and thrust give (<see cref="ReactiveAttitude"/>), and it plays its synthesized motor voice at the
/// static run-up rpm of the throttle (<see cref="StaticRunUp"/>).
/// </summary>
public partial class MenuAircraftView : ControlPreview
{
    const int SampleRate = 44100;
    // Same generator buffer as the flight and sound-screen voices (rounded up by Godot to 2048 frames).
    const float BufferSeconds = 0.04f;

    /// <summary>Rest heading: nose toward the camera (which looks north) and to its left.</summary>
    const double HeadingDeg = 205;
    const float FovDeg = 30f;
    const float CameraElevationDeg = 18f;

    /// <summary>Sky tilt that drops the horizon below the aircraft, so it is seen against the sky.</summary>
    const float SkyTiltDeg = -30f;

    readonly EngineSynth _synth = new(SampleRate);
    readonly GeneratorFeeder _feeder;
    System.Func<AudioSettings> _audio = () => new AudioSettings();
    AudioStreamPlayer _voice = null!;
    AudioStreamGeneratorPlayback? _playback;
    SoundSpec _spec = SoundSpec.Default;
    ReactiveAttitude? _reaction;
    StaticRunUp? _runUp;
    double _throttle;
    double _maxForward;

    public MenuAircraftView() => _feeder = new GeneratorFeeder(_synth);

    /// <param name="audio">Read every frame, so the sound screen's mix applies here too.</param>
    public void Init(Vector2 size, System.Func<AudioSettings> audio)
    {
        Init(size);
        _audio = audio;
        Camera.Fov = FovDeg;
        Camera.Far = 200f;
        _voice = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = BufferSeconds },
            Bus = AudioBuses.Aircraft,
        };
        AddChild(_voice);
    }

    protected override void BuildScenery(SubViewport scene)
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.22f, 0.42f, 0.75f),
            SkyHorizonColor = new Color(0.66f, 0.77f, 0.88f),
            GroundHorizonColor = new Color(0.55f, 0.62f, 0.50f),
            GroundBottomColor = new Color(0.20f, 0.36f, 0.16f),
            GroundCurve = 0.04f,
            SunAngleMax = 20f,
        };
        scene.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = sky },
                SkyRotation = new Vector3(Mathf.DegToRad(SkyTiltDeg), 0, 0),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.45f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.2f };
        scene.AddChild(sun);
        // High, behind the camera's left shoulder, so the side and front the camera sees are lit.
        sun.LookAtFromPosition(new Vector3(-4, 8, 6), Vector3.Zero, Vector3.Up);
    }

    public void ShowAircraft(AircraftDefinition definition, SoundSpec spec)
    {
        _spec = spec;
        ShowAircraft(definition);
    }

    protected override void OnAircraftShown(AircraftDefinition definition)
    {
        _maxForward = System.Math.Min(ReactiveAttitude.DefaultMaxForward, HullSize);
        _reaction = new ReactiveAttitude(definition, _maxForward);
        _runUp = definition.Power is { } power ? new StaticRunUp(power, _spec) : null;
        _throttle = 0;
        _synth.Reset();
    }

    protected override RigidBodyState DisplayState(double dt, in ControlInputs inputs)
    {
        _throttle = inputs.Throttle;
        if (_reaction is null || Aircraft is null) return AtRest;
        _reaction.Step(dt, Aircraft.Deflections, inputs.Throttle);
        return ReactiveAttitude.Pose(_reaction.Current, Vec3.Zero, Angle.Rad(HeadingDeg));
    }

    protected override double PropRpm(in ControlInputs inputs) => _runUp?.At(inputs.Throttle).Rpm ?? 0;

    protected override void PlaceCamera(double time)
    {
        // Fixed: aimed between the rest position and the end of the forward travel, far enough to keep the hull
        // (with its bank, pitch and yaw) and the whole travel in frame.
        var rest = ReactiveAttitude.Pose(default, Vec3.Zero, Angle.Rad(HeadingDeg));
        var centre = rest.Position + rest.Orientation.Rotate(HullCentre + BodyAxes.Forward * (0.5 * _maxForward));
        var target = centre.WorldToGodot();
        float halfExtent = (float)(0.4 * HullSize + 0.35 * _maxForward);
        float distance = halfExtent / Mathf.Tan(Mathf.DegToRad(FovDeg / 2));
        float elevation = Mathf.DegToRad(CameraElevationDeg);
        // The camera looks north (Godot −Z), so it stands to the south (Godot +Z).
        var eye = target + new Vector3(0, distance * Mathf.Sin(elevation), distance * Mathf.Cos(elevation));
        Camera.LookAtFromPosition(eye, target, Vector3.Up);
    }

    public override void _Process(double delta)
    {
        if (_playback is null)
        {
            // Silent until the throttle first opens: no voice runs while the menu is only browsed. Nothing is heard
            // under `--headless` (dummy driver), and a playback started there is reported leaked at exit.
            if (_throttle <= 0 || _runUp is null || AudioBuses.Headless) return;
            // A flight left while paused leaves the Aircraft bus muted; the view would otherwise be silent.
            AudioBuses.SetAircraftMuted(false);
            _voice.Play();
            _playback = (AudioStreamGeneratorPlayback)_voice.GetStreamPlayback();
        }
        _synth.Mix = VoiceMix.From(_audio());
        _feeder.Push(_playback, _runUp?.Synth(_throttle) ?? SynthParams.Silent);
    }

    // The generated voice never ends on its own: stop it before the tree tears down, or Godot reports its playback
    // object as leaked at exit.
    public override void _ExitTree()
    {
        _voice.Stop();
        _playback?.Dispose();
        _playback = null;
        _voice.Stream = null;
    }
}
