using SimLab.Flight.Airframe;

namespace SimLab.App.Tests;

internal static class TestData
{
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    public static AircraftDefinition Aircraft(string id) => AircraftLoader.Load(Path.Combine(RepoRoot, "aircraft", id));
}
