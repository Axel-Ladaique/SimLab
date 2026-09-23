using Godot;

namespace Symlab.Game;

public static class AppPaths
{
    public static string GameRoot => ProjectSettings.GlobalizePath("res://");
    public static string RepoRoot => System.IO.Path.GetFullPath(System.IO.Path.Combine(GameRoot, ".."));
    public static string AircraftRoot => System.Environment.GetEnvironmentVariable("SYMLAB_AIRCRAFT_DIR") ?? System.IO.Path.Combine(RepoRoot, "aircraft");
    public static string TranslationsCsv => System.IO.Path.Combine(GameRoot, "translations", "strings.csv");
    public static string UserDir => OS.GetUserDataDir();
    public static string SettingsFile => System.IO.Path.Combine(UserDir, "settings.json");
    public static string RadioDir => System.IO.Path.Combine(UserDir, "radios");
    public static string RecordingsDir => System.IO.Path.Combine(UserDir, "recordings");
}
