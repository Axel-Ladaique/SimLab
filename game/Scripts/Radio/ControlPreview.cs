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
/// dynamics never run: the body stays at rest.
/// </summary>
public partial class ControlPreview : SubViewportContainer
{
    /// <summary>Camera swing either side of straight behind, and its period.</summary>
    const double SwingDeg = 25;
    const double SwingSeconds = 16;

    static readonly RigidBodyState AtRest = new(Vec3.Zero, Vec3.Zero, Quat.Identity, Vec3.Zero);

    SubViewport _viewport = null!;
    Camera3D _camera = null!;
    AircraftVisual? _visual;
    Vector3 _target;
    float _distance = 3f;
    double _time;

    public Aircraft? Aircraft { get; private set; }

    public void Init(Vector2 size)
    {
        Stretch = true;
        CustomMinimumSize = size;
        _viewport = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(_viewport);

        _viewport.AddChild(new WorldEnvironment
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
        _viewport.AddChild(sun);
        sun.LookAtFromPosition(new Vector3(2, 5, 3), Vector3.Zero, Vector3.Up);

        _camera = new Camera3D { Current = true, Fov = 40f, Near = 0.02f, Far = 100f };
        _viewport.AddChild(_camera);
    }

    public void ShowAircraft(AircraftDefinition definition)
    {
        _visual?.QueueFree();
        Aircraft = new Aircraft(definition);
        Aircraft.Reset(AtRest);
        _visual = new AircraftVisual();
        _viewport.AddChild(_visual);
        _visual.Build(AircraftMeshBuilder.Build(definition, Aircraft.Aero.Segments));
        _visual.UpdateFrom(Aircraft, AtRest);

        // Frame the hull: centre of its bounding box, distance from the larger of the half-span and the length.
        var points = definition.Hull.Select(h => h.Position).DefaultIfEmpty(Vec3.Zero).ToList();
        var min = new Vec3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z));
        var max = new Vec3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
        _target = AtRest.Orientation.Rotate((min + max) * 0.5).WorldToGodot();
        double size = System.Math.Max(System.Math.Max(max.Y - min.Y, max.X - min.X), 0.5);
        _distance = (float)(1.2 * size);
        PlaceCamera();
    }

    /// <summary>Moves the servos toward the commands and advances the camera swing.</summary>
    public void Step(double dt, in ControlInputs inputs)
    {
        if (Aircraft is null || _visual is null) return;
        Aircraft.StepControls(dt, inputs);
        _visual.UpdateFrom(Aircraft, AtRest);
        _time += System.Math.Min(dt, 0.1); // no jump after a loading hitch
        PlaceCamera();
    }

    void PlaceCamera()
    {
        // Starts 3/4 rear-right, swings to 3/4 rear-left and back.
        double swing = Angle.Rad(SwingDeg) * System.Math.Cos(2 * System.Math.PI * _time / SwingSeconds);
        var back = AtRest.Orientation.Rotate(-BodyAxes.Forward).WorldToGodot();
        var right = AtRest.Orientation.Rotate(BodyAxes.Right).WorldToGodot();
        var direction = (back * (float)System.Math.Cos(swing) + right * (float)System.Math.Sin(swing)).Normalized();
        var eye = _target + direction * _distance + Vector3.Up * (_distance * 0.3f);
        _camera.LookAtFromPosition(eye, _target, Vector3.Up);
    }
}
