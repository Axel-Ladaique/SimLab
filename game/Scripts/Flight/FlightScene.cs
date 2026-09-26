using System.Linq;
using Godot;
using SimLab.App.Audio;
using SimLab.App.Cameras;
using SimLab.App.Field;
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Recording;
using SimLab.Game.Audio;
using SimLab.Game.Radio;
using SimLab.Game.World;
using SimLab.Input;

namespace SimLab.Game.Flight;

/// <summary>One flight: polls input, advances the session, draws the aircraft from the pilot's eyes.</summary>
public partial class FlightScene : Node3D
{
    Services _services = null!;
    FlightSession _session = null!;
    AircraftVisual _visual = null!;
    Camera3D _camera = null!;
    CameraDirector _cameras = null!;
    int _resetCount;
    System.Func<double, double, double> _terrainHeight = null!;
    // True right after the camera has just been teleported to a new position (a view change or a flight reset):
    // consumed after the next placement to re-track Doppler without reading the teleport as a velocity spike.
    bool _retrackDoppler;
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
    public CameraView CameraView => _cameras.Current;

    /// <summary>Whether the OSD is on: the pilot's choice, always on in scripted runs (screenshots, smoke tests).</summary>
    public bool HudShown => _script is not null || _services.Settings.ShowFlightData;

    public void Init(Services services, string aircraftId, System.Action exit, System.Func<double, ControlInputs>? script = null)
    {
        _services = services;
        _exit = exit;
        _script = script;
        var definition = AircraftLoader.Load(System.IO.Path.Combine(AppPaths.AircraftRoot, aircraftId));
        _session = new FlightSession(definition, services.Settings.Conditions);
        services.Router.ResetForNewFlight();

        _windsock = FieldBuilder.Build(this, _session.Terrain, services.Settings.Conditions).Windsock;
        _visual = new AircraftVisual();
        AddChild(_visual);
        _visual.Build(AircraftMeshBuilder.Build(definition, _session.Aircraft.Aero.Segments));
        // Place the visual (and therefore the audio node, its child) at the spawn point before the audio node is
        // added, so its first Doppler-tracked position isn't the world origin (which would read as a spike).
        _visual.UpdateFrom(_session.Aircraft, _session.DisplayState);

        AudioBuses.Apply(services.Settings);
        var audio = new AircraftAudio();
        _visual.AddChild(audio);
        audio.Init(_session, SoundSpecLoader.Load(System.IO.Path.Combine(AppPaths.AircraftRoot, aircraftId)), () => services.Settings.Audio);
        AddChild(new FieldAmbience());

        _terrainHeight = _session.Terrain.Height;
        var pilot = ClubField.PilotPosition;
        var eye = new Vec3(pilot.X, pilot.Y, _terrainHeight(pilot.X, pilot.Y) + ClubField.EyeHeight);
        var ground = new LineOfSightRig(eye, services.Settings.FovDeg, services.Settings.AutoZoom);
        // Scripted runs (smoke tests, screenshots) always start in the pilot-box ground view, never the user's
        // saved view, so the shown frame doesn't depend on whoever last played; --view then switches it.
        var initialView = script is null ? services.Settings.CameraView : CameraView.Ground;
        _cameras = new CameraDirector(ground, new FpvRig(FpvCameraSpec.For(definition)), new ChaseRig(), initialView);
        _cameras.Reset(Context());
        _resetCount = _session.ResetCount;
        _retrackDoppler = true;
        _camera = new Camera3D { Current = true, Near = 0.1f, Far = 4000f, Fov = (float)services.Settings.FovDeg };
        AddChild(_camera);
        _hud = new FlightHud();
        _hud.Init(ToggleHud, NextCamera, manageMouse: script is null);
        AddChild(_hud);
        _crash = new CrashOverlay();
        AddChild(_crash);
        _diagnostics = new DiagnosticsOverlay();
        AddChild(_diagnostics);
        if (services.Settings.RecordFlights && script is null) StartRecording(aircraftId, definition);
    }

    /// <summary>Ground → FPV → chase → ground; remembered for the next flight.</summary>
    public void NextCamera()
    {
        _cameras.Next(Context());
        _retrackDoppler = true;
        RememberCamera();
    }

    /// <summary>Jumps to a view; remembered for the next flight.</summary>
    public void SelectCamera(CameraView view)
    {
        _cameras.Select(view, Context());
        _retrackDoppler = true;
        RememberCamera();
    }

    /// <summary>Switches the view without remembering it (command-line screenshots).</summary>
    public void ShowCamera(CameraView view)
    {
        _cameras.Select(view, Context());
        _retrackDoppler = true;
    }

    /// <summary>Shows or hides the OSD and remembers it (H key, HUD button). Scripted runs keep it on.</summary>
    public void ToggleHud()
    {
        if (_script is not null) return;
        _services.Settings = _services.Settings with { ShowFlightData = !_services.Settings.ShowFlightData };
        _services.SaveSettings();
    }

    void RememberCamera()
    {
        if (_services.Settings.CameraView == _cameras.Current) return;
        _services.Settings = _services.Settings with { CameraView = _cameras.Current };
        _services.SaveSettings();
    }

    public override void _Process(double delta)
    {
        ulong start = Time.GetTicksUsec();
        LastInput = _script is null
            ? _services.Router.Update(delta, JoypadReader.Poll(), KeyboardInput.Keys(), KeyboardInput.Commands())
            : new RouterOutput(_script(_session.Simulation.Time), [], InputSource.Keyboard, "script");
        foreach (var action in LastInput.Actions)
        {
            if (action == SwitchAction.NextCamera && _script is null) NextCamera();
            else _session.Handle(action);
        }
        if (_session.Recorder is { } recorder) recorder.RawChannels = LastInput.RawFrame?.Axes;
        LastSteps = _session.Tick(delta, LastInput.Controls);
        _latency.Add((Time.GetTicksUsec() - start) / 1e6, delta, _session.Simulation.InterpolationAlpha);

        _visual.UpdateFrom(_session.Aircraft, _session.DisplayState);
        if (_session.ResetCount != _resetCount)
        {
            _resetCount = _session.ResetCount;
            _cameras.Reset(Context());
            _retrackDoppler = true;
        }
        var pose = _cameras.Update(delta, Context());
        _camera.SetCullMaskValue(AircraftVisual.Layer, _cameras.Current != CameraView.Fpv);
        var from = pose.Position.WorldToGodot();
        var to = pose.LookAt.WorldToGodot();
        var up = pose.Up.WorldToGodot();
        if (Mathf.Abs((to - from).Normalized().Dot(up.Normalized())) > 0.999f) up = Vector3.Back;
        _camera.Fov = (float)pose.VerticalFovDeg;
        _camera.LookAtFromPosition(from, to, up);
        if (_retrackDoppler)
        {
            // Setting DopplerTracking resets Godot's internal velocity tracker to the camera's current position,
            // so doing this right after the placement above means the next frame's velocity estimate starts from
            // here rather than from wherever the camera used to be — the teleport never reads as a spike.
            _camera.DopplerTracking = Camera3D.DopplerTrackingEnum.Disabled;
            _camera.DopplerTracking = Camera3D.DopplerTrackingEnum.IdleStep;
            _retrackDoppler = false;
        }
        _windsock.Apply(Windsock.Pose(_session.Simulation.Environment.Wind.At(WindsockNode.PoleHeight)));
        var osd = OsdData.From(_session.Aircraft, _session.DisplayState, _session.HeightAgl, _session.FlightTime,
            LastInput.Controls.Throttle, ClubField.PilotPosition, LastInput.Controls.Flap);
        _hud.UpdateHud(_session, LastInput, osd, HudShown, _cameras.Current);
        _crash.UpdateCrash(_session.Aircraft.Crash);
        _diagnostics.UpdateDiagnostics(this);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.H })
        {
            GetViewport().SetInputAsHandled();
            ToggleHud();
            return;
        }
        if (!@event.IsActionPressed("ui_cancel")) return;
        GetViewport().SetInputAsHandled();
        _exit();
    }

    public override void _ExitTree() => _session.Dispose();

    CameraContext Context()
    {
        var display = _session.DisplayState;
        // Init runs before the scene enters the tree (Main.StartFlight), when there is no viewport yet.
        var size = IsInsideTree() ? GetViewport().GetVisibleRect().Size : Vector2.Zero;
        double aspect = size.Y > 0 ? size.X / size.Y : 16.0 / 9.0;
        return new CameraContext(display.Position, display.Orientation, _session.Span, _terrainHeight, aspect);
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
