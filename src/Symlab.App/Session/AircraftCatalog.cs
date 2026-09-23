using Symlab.Flight.Airframe;

namespace Symlab.App.Session;

public readonly record struct AircraftEntry(string Id, string Name, string Description);

public static class AircraftCatalog
{
    public static IReadOnlyList<AircraftEntry> List(string aircraftRoot, out IReadOnlyList<string> errors)
    {
        var found = new List<AircraftEntry>();
        var problems = new List<string>();
        if (Directory.Exists(aircraftRoot))
        {
            foreach (var folder in Directory.GetDirectories(aircraftRoot).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (!File.Exists(Path.Combine(folder, "aircraft.json"))) continue;
                try
                {
                    var def = AircraftLoader.Load(folder);
                    found.Add(new AircraftEntry(Path.GetFileName(folder), def.Name, def.Description));
                }
                catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or ArgumentException)
                {
                    problems.Add(ex.Message);
                }
            }
        }
        errors = problems;
        return found;
    }
}
