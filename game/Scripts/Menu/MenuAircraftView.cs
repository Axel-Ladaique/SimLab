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
using SimLab.Game.Radio;
using SimLab.Game.World;

namespace SimLab.Game.Menu;

/// <summary>
/// The main menu's full-window live view: the chosen field (sky, terrain, runway, trees, windsock, sun of the menu's
/// conditions) with the selected aircraft flying in place in front of the camera, framed right of the menu panel.
/// Its surfaces follow the radio or keyboard through the aircraft's own mixing and servos (like the radio screen's
/// <see cref="ControlPreview"/>), it banks, pitches, yaws and creeps forward within small limits in the direction its
/// control moments and thrust give (<see cref="ReactiveAttitude"/>), and its propeller spins at the static run-up rpm
/// of the throttle (<see cref="StaticRunUp"/>). The motor is not heard here: the home screen only plays the field
/// ambience. The sun and the windsock follow the conditions live.
/// </summary>
public partial class MenuAircraftView : ControlPreview
{
    /// <summary>Rest heading: nose toward the camera (which looks north) and to its left, into the picture.</summary>
    const double HeadingDeg = 205;
    /// <summary>Where the aircraft flies in place (world ENU): south-east of the pilot box, some 18 m south of the
    /// windsock and a few metres up. The camera, south of it and looking north, sees the windsock just left of the
    /// aircraft (right of the menu panel), then the runway and the tree line beyond.</summary>
    static readonly Vec3 Origin = new(20, -46, 4);
    const float FovDeg = 40f;
    /// <summary>Camera slightly above the aircraft: a banked wing is not seen edge-on and the horizon sits in the
    /// upper part of the image.</summary>
    const float CameraElevationDeg = 10f;
    /// <summary>Fraction of the half-height of the image the aircraft's framing extent fills.</summary>
    const float FrameFill = 0.5f;
    /// <summary>Horizontal place of the aircraft in the image, −1 left edge to 1 right edge: right of the menu panel.</summary>
    const float ScreenX = 0.45f;

    SoundSpec _spec = SoundSpec.Default;
    ReactiveAttitude? _reaction;
    StaticRunUp? _runUp;
    double _maxForward;
    FlightConditions _conditions = new();
    FieldNodes? _field;

    /// <param name="conditions">Sun and wind of the field when the menu opens.</param>
    public void Init(FlightConditions conditions)
    {
        _conditions = conditions; // read by BuildScenery, which the base Init calls
        Init(Vector2.Zero); // covers the parent's area in physical pixels: see FitToPixels
        Camera.Fov = FovDeg;
        Camera.Near = 0.1f;
        Camera.Far = 4000f;
    }

    protected override void BuildScenery(SubViewport scene)
    {
        var root = new Node3D();
        scene.AddChild(root);
        _field = FieldBuilder.Build(root, new ClubFieldTerrain(TreePlanter.Plant(FlightSession.TreeSeed)), _conditions);
    }

    // The windsock builds its sock in its own _Ready, so its pose can only be applied once it is in the tree.
    public override void _Ready()
    {
        FitToPixels();
        ApplyConditions(_conditions);
    }

    /// <summary>
    /// Covers the parent's area with a scene rendered at the window's physical resolution. A stretched container
    /// sizes its viewport in canvas units, which the window's canvas_items stretch then scales: on a window larger
    /// than the 1600×900 base (full screen, Retina) the field would be upscaled and blurred under sharp UI text.
    /// So the container is sized in physical pixels and scaled back down by the same factor.
    /// </summary>
    void FitToPixels()
    {
        var scale = GetViewport().GetFinalTransform().Scale;
        var pixels = (GetParentAreaSize() * scale).Round();
        if (Size != pixels) Size = pixels;
        Scale = Vector2.One / scale;
    }

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
    }

    protected override RigidBodyState DisplayState(double dt, in ControlInputs inputs)
    {
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

    public override void _Process(double delta) => FitToPixels();
}
