using System.Linq;
using Godot;
using SimLab.App.Audio;
using SimLab.App.Cameras;
using SimLab.App.Field;
using SimLab.App.Session;
using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Recording;
using SimLab.Game.Audio;
using SimLab.Game.Radio;
using SimLab.Game.World;

namespace SimLab.Game.Flight;

/// <summary>One flight: polls input, advances the session, draws the aircraft from the pilot's eyes.</summary>
public partial class FlightScene : Node3D
{
    Services _services = null!;
    FlightSession _session = null!;
    AircraftVisual _visual = null!;
    Camera3D _camera = null!;
    ICameraRig _rig = null!;
    WindsockNode _windsock = null!;
    FlightHud _hud = null!;
    CrashOverlay _crash = null!;
    DiagnosticsOverlay _diagnostics = null!;
    System.Action _exit = null!;
    System.Func<double, ControlInputs>? _script;
    readonly LatencyMeter _latency = new();

    public FlightSession Session => _session;
    public LatencyMeter Latency => _latency;
    public DiagnosticsOverlay Diagnostics => _diagnostics;
    public RouterOutput LastInput { get; private set; }
    public int LastSteps { get; private set; }

    public void Init(Services services, string aircraftId, System.Action exit, System.Func<double, ControlInputs>? script = null, StartMode mode = StartMode.Normal)
    {
        _services = services;
        _exit = exit;
        _script = script;
        var definition = AircraftLoader.Load(System.IO.Path.Combine(AppPaths.AircraftRoot, aircraftId));
        _session = new FlightSession(definition, services.Settings.Conditions, mode);
        services.Router.ResetForNewFlight();

        _windsock = FieldBuilder.Build(this, _session.Terrain, services.Settings.Conditions);
        _visual = new AircraftVisual();
        AddChild(_visual);
        _visual.Build(AircraftMeshBuilder.Build(definition, _session.Aircraft.Aero.Segments));
        // Place the visual (and therefore the audio node, its child) at the spawn point before the audio node is
        // added, so its first Doppler-tracked position isn't the world origin (which would read as a spike).
        _visual.UpdateFrom(_session.Aircraft, _session.DisplayState);

        AudioBuses.Apply(services.Settings);
        var audio = new AircraftAudio();
        _visual.AddChild(audio);
        audio.Init(_session, SoundSpecLoader.Load(System.IO.Path.Combine(AppPaths.AircraftRoot, aircraftId)));
        AddChild(new FieldAmbience());

        if (mode == StartMode.GroundCheck)
        {
            _rig = new OrbitRig(services.Settings.FovDeg);
        }
        else
        {
            var pilot = ClubField.PilotPosition;
            var eye = new Vec3(pilot.X, pilot.Y, _session.Terrain.Height(pilot.X, pilot.Y) + ClubField.EyeHeight);
            _rig = new LineOfSightRig(eye, services.Settings.FovDeg, services.Settings.AutoZoom);
        }
        _rig.Reset(Context());
        _camera = new Camera3D { Current = true, Near = 0.1f, Far = 4000f, Fov = (float)services.Settings.FovDeg };
        AddChild(_camera);
        _hud = new FlightHud();
        AddChild(_hud);
        _crash = new CrashOverlay();
        AddChild(_crash);
        _diagnostics = new DiagnosticsOverlay();
        AddChild(_diagnostics);
        if (services.Settings.RecordFlights && script is null && mode == StartMode.Normal) StartRecording(aircraftId, definition);
    }

    public override void _Process(double delta)
    {
        ulong start = Time.GetTicksUsec();
        LastInput = _script is null
            ? _services.Router.Update(delta, JoypadReader.Poll(), KeyboardInput.Keys(), KeyboardInput.Commands())
            : new RouterOutput(_script(_session.Simulation.Time), [], InputSource.Keyboard, "script");
        foreach (var action in LastInput.Actions) _session.Handle(action);
        if (_session.Recorder is { } recorder) recorder.RawChannels = LastInput.RawFrame?.Axes;
        LastSteps = _session.Tick(delta, LastInput.Controls);
        _latency.Add((Time.GetTicksUsec() - start) / 1e6, delta, _session.Simulation.InterpolationAlpha);

        _visual.UpdateFrom(_session.Aircraft, _session.DisplayState);
        var pose = _rig.Update(delta, Context());
        var from = pose.Position.WorldToGodot();
        var to = pose.LookAt.WorldToGodot();
        var up = Mathf.Abs((to - from).Normalized().Y) > 0.999f ? Vector3.Back : Vector3.Up;
        _camera.Fov = (float)pose.VerticalFovDeg;
        _camera.LookAtFromPosition(from, to, up);
        _windsock.Apply(Windsock.Pose(_session.Simulation.Environment.Wind.At(WindsockNode.PoleHeight)));
        _hud.UpdateHud(_session, LastInput, _services.Settings.ShowFlightData);
        _crash.UpdateCrash(_session.Aircraft.Crash);
        _diagnostics.UpdateDiagnostics(this);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel")) return;
        GetViewport().SetInputAsHandled();
        _exit();
    }

    public override void _ExitTree() => _session.Dispose();

    CameraContext Context()
    {
        var display = _session.DisplayState;
        return new CameraContext(display.Position, display.Orientation, _session.Span);
    }

    /// <summary>Raw radio axes recorded next to the processed controls (columns raw_axis0…).</summary>
    static readonly string[] RawChannelNames = Enumerable.Range(0, JoypadReader.MaxAxes).Select(i => $"axis{i}").ToArray();

    void StartRecording(string aircraftId, AircraftDefinition definition)
    {
        System.IO.StreamWriter? writer = null;
        try
        {
            System.IO.Directory.CreateDirectory(AppPaths.RecordingsDir);
            var path = System.IO.Path.Combine(AppPaths.RecordingsDir, $"{System.DateTime.Now:yyyyMMdd-HHmmss-fff}-{aircraftId}.csv");
            writer = new System.IO.StreamWriter(path);
            _session.AttachRecorder(new FlightRecorder(writer, definition, rawChannelNames: RawChannelNames));
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            writer?.Dispose();
            GD.PushWarning($"Flight recording disabled: {ex.Message}");
        }
    }
}
