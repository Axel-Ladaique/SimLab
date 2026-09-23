using System.Linq;
using Godot;
using Symlab.App.Session;
using Symlab.App.Settings;
using Symlab.Game.Radio;

namespace Symlab.Game;

public partial class Main : Node
{
    Services _services = null!;
    Node? _current;

    public override void _Ready()
    {
        var settings = AppSettings.Load(AppPaths.SettingsFile);
        Translations.Register(AppPaths.TranslationsCsv, settings.Language);
        var store = new RadioProfileStore(AppPaths.RadioDir);
        _services = new Services { Settings = settings, Radios = store, Router = new InputRouter(guid => store.Load(guid, out _)) };

        var args = OS.GetCmdlineUserArgs();
        if (Has(args, "--smoke-boot"))
        {
            GD.Print($"SYMLAB_BOOT_OK locale={TranslationServer.GetLocale()} title={Tr("APP_TITLE")}");
            GetTree().Quit(0);
            return;
        }
        if (Has(args, "--smoke-radio"))
        {
            var pads = JoypadReader.Poll();
            GD.Print($"SYMLAB_RADIO_OK joypads={pads.Count}");
            foreach (var pad in pads)
                GD.Print($"JOYPAD guid={pad.Guid} name={pad.Name} axes={string.Join(";", pad.Frame.Axes.Select(a => a.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}");
            GetTree().Quit(0);
            return;
        }
        if (ArgValue(args, "--screenshot-field") is { } fieldShot)
        {
            var preview = new Node3D();
            AddChild(preview);
            var terrain = new Symlab.App.Field.ClubFieldTerrain(Symlab.App.Field.TreePlanter.Plant(FlightSession.TreeSeed));
            World.FieldBuilder.Build(preview, terrain, _services.Settings.Conditions);
            var eye = Symlab.App.Field.ClubField.PilotPosition.ToGodot() + new Vector3(0, (float)Symlab.App.Field.ClubField.EyeHeight, 0);
            var camera = new Camera3D { Current = true, Fov = (float)_services.Settings.FovDeg, Far = 4000f };
            preview.AddChild(camera);
            // Aim between the runway's east half and the windsock so the screenshot keeps both in frame
            // (the windsock sits close to the pilot, well off the runway's own axis).
            var runwayEastQuarter = new Vector3((float)(Symlab.App.Field.ClubField.RunwayLength / 4), 4f, 0f);
            var windsockAim = Symlab.App.Field.ClubField.WindsockPosition.ToGodot() + new Vector3(0, 3f, 0);
            camera.LookAtFromPosition(eye, (runwayEastQuarter + windsockAim) / 2f, Vector3.Up);
            CaptureAfterFrames(20, fieldShot);
            return;
        }
        if (ArgValue(args, "--screenshot-aircraft") is { } aircraftId && args.Length > System.Array.IndexOf(args, "--screenshot-aircraft") + 2)
        {
            string shotPath = args[System.Array.IndexOf(args, "--screenshot-aircraft") + 2];
            var def = Symlab.Flight.Airframe.AircraftLoader.Load(System.IO.Path.Combine(AppPaths.AircraftRoot, aircraftId));
            var session = new FlightSession(def, _services.Settings.Conditions);
            var preview = new Node3D();
            AddChild(preview);
            World.FieldBuilder.Build(preview, session.Terrain, _services.Settings.Conditions);
            var visual = new Flight.AircraftVisual();
            preview.AddChild(visual);
            visual.Build(Symlab.App.Visual.AircraftMeshBuilder.Build(def, session.Aircraft.Aero.Segments));
            var start = session.StartState();
            visual.UpdateFrom(session.Aircraft, start);
            visual.SetAllDeflections(0.35);
            var p = start.Position.ToGodot();
            var forward = start.Orientation.Rotate(Symlab.Flight.Geometry.Vec3.UnitX).ToGodot();
            var left = -start.Orientation.Rotate(Symlab.Flight.Geometry.Vec3.UnitZ).ToGodot();
            var camera = new Camera3D { Current = true, Fov = 50f, Near = 0.05f, Far = 4000f };
            preview.AddChild(camera);
            camera.LookAtFromPosition(p + forward * 2.5f + left * 3f + Vector3.Up * 1.2f, p, Vector3.Up);
            CaptureAfterFrames(20, shotPath);
            return;
        }
        ShowRadio();
    }

    public void ShowRadio()
    {
        var screen = new RadioScreen();
        screen.Init(_services, ShowRadio);
        Switch(screen);
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

    async void CaptureAfterFrames(int frames, string path)
    {
        for (int i = 0; i < frames; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"SYMLAB_SCREENSHOT path={path} error={error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
