using Godot;
using SimLab.App.Audio;
using SimLab.App.Field;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Controls;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;
using SimLab.Game.Audio;
using SimLab.Game.Radio;
using SimLab.Game.World;

namespace SimLab.Game.Menu;

/// <summary>
/// The main menu's full-window live view: the chosen field (sky, terrain, runway, trees, windsock, sun of the menu's
/// conditions) with the selected aircraft flying in place in front of the camera, framed right of the menu panel.
/// Its surfaces follow the radio or keyboard through the aircraft's own mixing and servos (like the radio screen's
/// <see cref="ControlPreview"/>), it banks, pitches, yaws and creeps forward within small limits in the direction its
/// control moments and thrust give (<see cref="ReactiveAttitude"/>), and it plays its synthesized motor voice at the
/// static run-up rpm of the throttle (<see cref="StaticRunUp"/>). The sun and the windsock follow the conditions live.
/// </summary>
public partial class MenuAircraftView : ControlPreview
{
    const int SampleRate = 44100;
    // Same generator buffer as the flight and sound-screen voices (rounded up by Godot to 2048 frames).
    const float BufferSeconds = 0.04f;

    /// <summary>Rest heading: nose toward the camera (which looks north) and to its left, into the picture.</summary>
    const double HeadingDeg = 205;
    /// <summary>Where the aircraft flies in place (world ENU): north-west of the pilot box and a few metres up, with
    /// the runway behind it as the camera sees it.</summary>
    static readonly Vec3 Origin = new(-6, -12, 4);
    const float FovDeg = 40f;
    /// <summary>Camera slightly above the aircraft: a banked wing is not seen edge-on and the horizon sits in the
    /// upper part of the image.</summary>
    const float CameraElevationDeg = 15f;
    /// <summary>Fraction of the half-height of the image the aircraft's framing extent fills.</summary>
    const float FrameFill = 0.5f;
    /// <summary>Horizontal place of the aircraft in the image, −1 left edge to 1 right edge: right of the menu panel.</summary>
    const float ScreenX = 0.45f;

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
    FlightConditions _conditions = new();
    FieldNodes? _field;

    public MenuAircraftView() => _feeder = new GeneratorFeeder(_synth);

    /// <param name="audio">Read every frame, so the sound screen's mix applies here too.</param>
    /// <param name="conditions">Sun and wind of the field when the menu opens.</param>
    public void Init(System.Func<AudioSettings> audio, FlightConditions conditions)
    {
        _conditions = conditions; // read by BuildScenery, which the base Init calls
        Init(Vector2.Zero);
        SetAnchorsPreset(LayoutPreset.FullRect);
        _audio = audio;
        Camera.Fov = FovDeg;
        Camera.Near = 0.1f;
        Camera.Far = 4000f;
        _voice = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = BufferSeconds },
            Bus = AudioBuses.Aircraft,
        };
        AddChild(_voice);
    }

    protected override void BuildScenery(SubViewport scene)
    {
        var root = new Node3D();
        scene.AddChild(root);
        _field = FieldBuilder.Build(root, new ClubFieldTerrain(TreePlanter.Plant(FlightSession.TreeSeed)), _conditions);
    }

    // The windsock builds its sock in its own _Ready, so its pose can only be applied once it is in the tree.
    public override void _Ready() => ApplyConditions(_conditions);

    /// <summary>Re-aims the sun and the windsock (steady wind at the top of its pole, no gusts).</summary>
    public void ApplyConditions(FlightConditions conditions)
    {
        _conditions = conditions;
        if (_field is not { } field) return;
        FieldBuilder.AimSun(field.Sun, conditions);
        if (!field.Windsock.IsInsideTree()) return;
        var wind = new WindField(conditions.ToWindSettings(), conditions.Seed).SteadyAt(WindsockNode.PoleHeight);
        field.Windsock.Apply(Windsock.Pose(wind));
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
        if (_reaction is null || Aircraft is null) return ReactiveAttitude.Pose(default, Origin, Angle.Rad(HeadingDeg));
        _reaction.Step(dt, Aircraft.Deflections, inputs.Throttle);
        return ReactiveAttitude.Pose(_reaction.Current, Origin, Angle.Rad(HeadingDeg));
    }

    protected override double PropRpm(in ControlInputs inputs) => _runUp?.At(inputs.Throttle).Rpm ?? 0;

    protected override void PlaceCamera(double time)
    {
        // Fixed: aimed between the rest position and the end of the forward travel, far enough to keep the hull
        // (with its bank, pitch and yaw) and the whole travel in its share of the frame.
        var rest = ReactiveAttitude.Pose(default, Origin, Angle.Rad(HeadingDeg));
        var centre = rest.Position + rest.Orientation.Rotate(HullCentre + BodyAxes.Forward * (0.5 * _maxForward));
        var target = centre.WorldToGodot();
        float halfExtent = (float)(0.4 * HullSize + 0.35 * _maxForward);
        float tanHalf = Mathf.Tan(Mathf.DegToRad(FovDeg / 2));
        float distance = halfExtent / (tanHalf * FrameFill);
        float elevation = Mathf.DegToRad(CameraElevationDeg);
        // The camera looks north (Godot −Z), so it stands to the south (Godot +Z).
        var eye = target + new Vector3(0, distance * Mathf.Sin(elevation), distance * Mathf.Cos(elevation));
        Camera.LookAtFromPosition(eye, target, Vector3.Up);
        // Slide the camera sideways (the horizon, at infinity, does not move) so the aircraft sits at ScreenX.
        var size = Scene.Size;
        float aspect = size.Y > 0 ? (float)size.X / size.Y : 16f / 9f;
        Camera.HOffset = -ScreenX * distance * tanHalf * aspect;
    }

    public override void _Process(double delta)
    {
        if (_playback is null)
        {
            // Silent until the throttle first opens: no voice runs while the menu is only browsed. Nothing is heard
            // under `--headless` (dummy driver), and a playback started there is reported leaked at exit.
            if (_throttle <= 0 || _runUp is null || AudioBuses.Headless) return;
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
