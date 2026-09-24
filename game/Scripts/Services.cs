using SimLab.App.Session;
using SimLab.App.Settings;

namespace SimLab.Game;

/// <summary>Application-wide objects shared by the screens.</summary>
public sealed class Services
{
    public required AppSettings Settings { get; set; }
    public required RadioProfileStore Radios { get; init; }
    public required InputRouter Router { get; init; }

    public void SaveSettings() => Settings.Save(AppPaths.SettingsFile);
}
