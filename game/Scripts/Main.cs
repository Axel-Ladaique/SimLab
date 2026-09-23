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
}
