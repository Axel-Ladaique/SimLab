using System.Globalization;
using System.Linq;
using Godot;
using SimLab.App.Field;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using SimLab.Game.Flight;
using SimLab.Game.Menu;
using SimLab.Game.Radio;
using SimLab.Game.World;

namespace SimLab.Game;

public partial class Main : Node
{
    Services _services = null!;
    Node? _current;
    double _smokeSeconds = -1;
    string? _smokeScreenshot;
    string _smokeAircraft = "";

    public override void _Ready()
    {
        var settings = AppSettings.Load(AppPaths.SettingsFile);
        Translations.Register(AppPaths.TranslationsCsv, settings.Language);
        var store = new RadioProfileStore(AppPaths.RadioDir);
        _services = new Services { Settings = settings, Radios = store, Router = new InputRouter(guid => store.Load(guid, out _)) };
        DisplaySettings.Apply(settings);
        if (!RunCommandLine(OS.GetCmdlineUserArgs())) ShowMenu();
    }

    public void ShowMenu() => ShowMenu(null);

    /// <param name="error">Shown at the top of the menu, e.g. why the last flight could not start.</param>
    public void ShowMenu(string? error)
    {
        var menu = new MainMenu();
        menu.Init(_services, id => StartFlight(id), ShowRadio, ShowSettings, () => GetTree().Quit(), error);
        Switch(menu);
    }

    public void ShowRadio()
    {
        var screen = new RadioScreen();
        screen.Init(_services, ShowMenu);
        Switch(screen);
    }

    public void ShowSettings()
    {
        var screen = new SettingsScreen();
        screen.Init(_services, ShowMenu, ShowSettings);
        Switch(screen);
    }

    public bool StartFlight(string aircraftId, System.Func<double, ControlInputs>? script = null)
    {
        var scene = new FlightScene();
        try
        {
            scene.Init(_services, aircraftId, ShowMenu, script);
        }
        catch (System.Exception ex)
        {
            scene.Free();
            GD.PushError($"Flight start failed for '{aircraftId}': {ex}");
            if (_smokeAircraft.Length > 0)
            {
                GD.Print($"SIMLAB_SMOKE_FAIL {ex.Message}");
                GetTree().Quit(1);
            }
            else ShowMenu($"{aircraftId}: {ex.Message}");
            return false;
        }
        Switch(scene);
        return true;
    }

    public override void _Process(double delta)
    {
        if (_smokeSeconds < 0 || _current is not FlightScene flight) return;
        var session = flight.Session;
        bool crashed = session.Aircraft.Crash != CrashCause.None;
        if (session.Simulation.Time < _smokeSeconds && !crashed) return;
        _smokeSeconds = -1;
        if (_smokeScreenshot is { } path)
        {
            CaptureAfterFrames(2, path);
            return;
        }
        var inv = CultureInfo.InvariantCulture;
        var p = session.Aircraft.State.Position;
        GD.Print($"SIMLAB_SMOKE_OK aircraft={_smokeAircraft} t={session.Simulation.Time.ToString("0.00", inv)} x={p.X.ToString("0.0", inv)} y={p.Y.ToString("0.00", inv)} z={p.Z.ToString("0.0", inv)} crash={session.Aircraft.Crash}");
        GetTree().Quit(0);
    }

    bool RunCommandLine(string[] args)
    {
        if (Has(args, "--smoke-boot"))
        {
            GD.Print($"SIMLAB_BOOT_OK locale={TranslationServer.GetLocale()} title={Tr("APP_TITLE")}");
            GetTree().Quit(0);
            return true;
        }
        if (Has(args, "--smoke-radio"))
        {
            var pads = JoypadReader.Poll();
            GD.Print($"SIMLAB_RADIO_OK joypads={pads.Count}");
            foreach (var pad in pads)
                GD.Print($"JOYPAD guid={pad.Guid} name={pad.Name} axes={string.Join(";", pad.Frame.Axes.Select(a => a.ToString("0.00", CultureInfo.InvariantCulture)))}");
            GetTree().Quit(0);
            return true;
        }
        if (Has(args, "--smoke-input-map"))
        {
            foreach (var action in new[] { "ui_left", "ui_right", "ui_up", "ui_down" })
                GD.Print($"INPUT_MAP {action} = {string.Join(" | ", InputMap.ActionGetEvents(action).Select(e => $"{e.GetClass()}({e.AsText()})"))}");
            GetTree().Quit(0);
            return true;
        }
                if (ArgValue(args, "--screen") == "radio")
        {
            ShowRadio();
            return true;
        }
        if (ArgValue(args, "--screenshot-menu") is { } menuShot)
        {
            ShowMenu();
            CaptureAfterFrames(20, menuShot);
            return true;
        }
        if (ArgValue(args, "--screenshot-settings") is { } settingsShot)
        {
            ShowSettings();
            CaptureAfterFrames(20, settingsShot);
            return true;
        }
        if (ArgValue(args, "--screenshot-field") is { } fieldShot)
        {
            PreviewField(fieldShot);
            return true;
        }
        int aircraftShot = System.Array.IndexOf(args, "--screenshot-aircraft");
        if (aircraftShot >= 0 && aircraftShot + 2 < args.Length)
        {
            PreviewAircraft(args[aircraftShot + 1], args[aircraftShot + 2]);
            return true;
        }
        int smoke = System.Array.IndexOf(args, "--smoke-flight");
        if (smoke >= 0 && smoke + 2 < args.Length)
        {
            _smokeAircraft = args[smoke + 1];
            _smokeSeconds = double.Parse(args[smoke + 2], CultureInfo.InvariantCulture);
            StartFlight(_smokeAircraft, _ => new ControlInputs(0.7, 0, 0, 0));
            return true;
        }
        foreach (var (flag, diagnostics) in new[] { ("--screenshot-flight", false), ("--screenshot-diagnostics", true) })
        {
            int shot = System.Array.IndexOf(args, flag);
            if (shot < 0 || shot + 3 >= args.Length) continue;
            _smokeAircraft = args[shot + 1];
            _smokeSeconds = double.Parse(args[shot + 2], CultureInfo.InvariantCulture);
            _smokeScreenshot = args[shot + 3];
            if (StartFlight(_smokeAircraft, t => new ControlInputs(1, 0, t > 3.5 && t < 5 ? 0.25 : 0.05, 0)) && diagnostics)
                ((FlightScene)_current!).Diagnostics.Shown = true;
            return true;
        }
        return false;
    }

    void PreviewField(string path)
    {
        var preview = new Node3D();
        Switch(preview);
        var terrain = new ClubFieldTerrain(TreePlanter.Plant(FlightSession.TreeSeed));
        FieldBuilder.Build(preview, terrain, _services.Settings.Conditions);
        var eye = ClubField.PilotPosition.ToGodot() + new Vector3(0, (float)ClubField.EyeHeight, 0);
        var camera = new Camera3D { Current = true, Fov = (float)_services.Settings.FovDeg, Far = 4000f };
        preview.AddChild(camera);
        // Aim between the runway's east half and the windsock so the screenshot keeps both in frame
        // (the windsock sits close to the pilot, well off the runway's own axis).
        var runwayEastQuarter = new Vector3((float)(ClubField.RunwayLength / 4), 4f, 0f);
        var windsockAim = ClubField.WindsockPosition.ToGodot() + new Vector3(0, 3f, 0);
        camera.LookAtFromPosition(eye, (runwayEastQuarter + windsockAim) / 2f, Vector3.Up);
        CaptureAfterFrames(20, path);
    }

    void PreviewAircraft(string aircraftId, string path)
    {
        var definition = AircraftLoader.Load(System.IO.Path.Combine(AppPaths.AircraftRoot, aircraftId));
        var session = new FlightSession(definition, _services.Settings.Conditions);
        var preview = new Node3D();
        Switch(preview);
        FieldBuilder.Build(preview, session.Terrain, _services.Settings.Conditions);
        var visual = new AircraftVisual();
        preview.AddChild(visual);
        visual.Build(AircraftMeshBuilder.Build(definition, session.Aircraft.Aero.Segments));
        var start = session.StartState();
        visual.UpdateFrom(session.Aircraft, start);
        visual.SetAllDeflections(0.35);
        var p = start.Position.ToGodot();
        var forward = start.Orientation.Rotate(Vec3.UnitX).ToGodot();
        var left = -start.Orientation.Rotate(Vec3.UnitZ).ToGodot();
        var camera = new Camera3D { Current = true, Fov = 50f, Near = 0.05f, Far = 4000f };
        preview.AddChild(camera);
        camera.LookAtFromPosition(p + forward * 2.5f + left * 3f + Vector3.Up * 1.2f, p, Vector3.Up);
        CaptureAfterFrames(20, path);
    }

    async void CaptureAfterFrames(int frames, string path)
    {
        for (int i = 0; i < frames; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"SIMLAB_SCREENSHOT path={path} error={error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    void Switch(Node next)
    {
        _current?.QueueFree();
        _current = next;
        AddChild(next);
    }

    static bool Has(string[] args, string flag) => System.Array.IndexOf(args, flag) >= 0;

    static string? ArgValue(string[] args, string flag)
    {
        int i = System.Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
