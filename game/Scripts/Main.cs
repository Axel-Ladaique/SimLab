using Godot;
using Symlab.App.Settings;

namespace Symlab.Game;

public partial class Main : Node
{
    public override void _Ready()
    {
        var settings = AppSettings.Load(AppPaths.SettingsFile);
        Translations.Register(AppPaths.TranslationsCsv, settings.Language);
        var args = OS.GetCmdlineUserArgs();
        if (System.Array.IndexOf(args, "--smoke-boot") >= 0)
        {
            GD.Print($"SYMLAB_BOOT_OK locale={TranslationServer.GetLocale()} title={Tr("APP_TITLE")}");
            GetTree().Quit(0);
        }
    }
}
