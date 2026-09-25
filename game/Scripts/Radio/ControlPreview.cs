using System.Linq;
using Godot;
using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Game.Flight;

namespace SimLab.Game.Radio;

/// <summary>
/// Small 3D view of an aircraft seen from 3/4 rear, slowly swinging from one side to the other, whose surfaces follow
/// the pilot commands through the aircraft's own mixing and servos (<see cref="Aircraft.StepControls"/>). The flight
/// dynamics never run: the body stays at rest. Subclasses change the scenery, the camera and the displayed pose
/// (see the main menu's live view).
/// </summary>
public partial class ControlPreview : SubViewportContainer
{
    /// <summary>Camera swing either side of straight behind, and its period.</summary>
    const double SwingDeg = 25;
    const double SwingSeconds = 16;

    protected static readonly RigidBodyState AtRest = new(Vec3.Zero, Vec3.Zero, Quat.Identity, Vec3.Zero);

    AircraftVisual? _visual;
    double _time;

    protected SubViewport Scene { get; private set; } = null!;
    protected Camera3D Camera { get; private set; } = null!;

    /// <summary>Centre of the hull's bounding box, body axes (CG-relative).</summary>
    protected Vec3 HullCentre { get; private set; }

    /// <summary>Larger of the span and the length of the hull, at least 0.5 m.</summary>
    protected double HullSize { get; private set; } = 0.5;

    public Aircraft? Aircraft { get; private set; }

    public void Init(Vector2 size)
    {
        Stretch = true;
        CustomMinimumSize = size;
        Scene = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(Scene);
        BuildScenery(Scene);
        Camera = new Camera3D { Current = true, Fov = 40f, Near = 0.02f, Far = 100f };
        Scene.AddChild(Camera);
    }

    /// <summary>Environment and lights of the view.</summary>
    protected virtual void BuildScenery(SubViewport scene)
    {
        scene.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.20f, 0.24f, 0.29f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.35f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.1f };
        scene.AddChild(sun);
        sun.LookAtFromPosition(new Vector3(2, 5, 3), Vector3.Zero, Vector3.Up);
    }

    public virtual void ShowAircraft(AircraftDefinition definition)
    {
        _visual?.QueueFree();
        Aircraft = new Aircraft(definition);
        Aircraft.Reset(AtRest);
        _visual = new AircraftVisual();
        Scene.AddChild(_visual);
        _visual.Build(AircraftMeshBuilder.Build(definition, Aircraft.Aero.Segments));

        var points = definition.Hull.Select(h => h.Position).DefaultIfEmpty(Vec3.Zero).ToList();
        var min = new Vec3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z));
        var max = new Vec3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
        HullCentre = (min + max) * 0.5;
        HullSize = System.Math.Max(System.Math.Max(max.Y - min.Y, max.X - min.X), 0.5);

        _time = 0;
        OnAircraftShown(definition);
        _visual.UpdateFrom(Aircraft, DisplayState(0, ControlInputs.Neutral), PropRpm(ControlInputs.Neutral));
        PlaceCamera(0);
    }

    /// <summary>Called once the aircraft is built, before its first pose.</summary>
    protected virtual void OnAircraftShown(AircraftDefinition definition) { }

    /// <summary>Moves the servos toward the commands, then updates the pose and the camera.</summary>
    public void Step(double dt, in ControlInputs inputs)
    {
        if (Aircraft is null || _visual is null) return;
        dt = System.Math.Min(dt, 0.1); // no jump after a loading hitch
        Aircraft.StepControls(dt, inputs);
        _visual.UpdateFrom(Aircraft, DisplayState(dt, inputs), PropRpm(inputs));
        _time += dt;
        PlaceCamera(_time);
    }

    /// <summary>Where the body is drawn (world ENU); at rest here.</summary>
    protected virtual RigidBodyState DisplayState(double dt, in ControlInputs inputs) => AtRest;

    /// <summary>Propeller speed shown; the power plant never runs here, so the disk stays hidden.</summary>
    protected virtual double PropRpm(in ControlInputs inputs) => 0;

    protected virtual void PlaceCamera(double time)
    {
        // Frame the hull: distance from the larger of the half-span and the length.
        var target = AtRest.Orientation.Rotate(HullCentre).WorldToGodot();
        float distance = (float)(1.2 * HullSize);
        // Starts 3/4 rear-right, swings to 3/4 rear-left and back.
        double swing = Angle.Rad(SwingDeg) * System.Math.Cos(2 * System.Math.PI * time / SwingSeconds);
        var back = AtRest.Orientation.Rotate(-BodyAxes.Forward).WorldToGodot();
        var right = AtRest.Orientation.Rotate(BodyAxes.Right).WorldToGodot();
        var direction = (back * (float)System.Math.Cos(swing) + right * (float)System.Math.Sin(swing)).Normalized();
        var eye = target + direction * distance + Vector3.Up * (distance * 0.3f);
        Camera.LookAtFromPosition(eye, target, Vector3.Up);
    }
}
