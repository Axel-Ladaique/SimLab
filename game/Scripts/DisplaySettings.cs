using Godot;
using SimLab.App.Settings;

namespace SimLab.Game;

public static class DisplaySettings
{
    /// <summary>Set for command-line runs (screenshots, smoke checks), which keep the project's 1600×900 window.</summary>
    public static bool ForceWindowed { get; set; }

    public static void Apply(AppSettings settings)
    {
        DisplayServer.WindowSetVsyncMode(settings.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        if (DisplayServer.GetName() == "headless") return;
        var mode = settings.Fullscreen && !ForceWindowed ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
    }
}
