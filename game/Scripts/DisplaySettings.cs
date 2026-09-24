using Godot;
using SimLab.App.Settings;

namespace SimLab.Game;

public static class DisplaySettings
{
    public static void Apply(AppSettings settings) =>
        DisplayServer.WindowSetVsyncMode(settings.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
}
