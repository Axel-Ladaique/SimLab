using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Sim;

namespace SimLab.Flight.Tests.Behavior;

internal static class Fleet
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

    public static AircraftDefinition Load(string id) => AircraftLoader.Load(Path.Combine(RepoRoot, "aircraft", id));

    /// <summary>
    /// The aircraft with its CG moved to <paramref name="cgX"/> (m from the datum, body x back): the aircraft.json is
    /// rewritten into a temporary folder next to a copy of the airfoils, so the loader measures every position from the new CG.
    /// </summary>
    public static AircraftDefinition LoadWithCgX(string id, double cgX)
    {
        var source = Path.Combine(RepoRoot, "aircraft", id);
        var root = Directory.CreateTempSubdirectory("simlab-cg-");
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, id)).FullName;
            foreach (var file in Directory.GetFiles(source, "*.json")) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            var airfoils = Directory.CreateDirectory(Path.Combine(root.FullName, "airfoils")).FullName;
            foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot, "aircraft", "airfoils"), "*.json"))
                File.Copy(file, Path.Combine(airfoils, Path.GetFileName(file)));
            var path = Path.Combine(folder, "aircraft.json");
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            json["cg"]![0] = cgX;
            File.WriteAllText(path, json.ToJsonString());
            return AircraftLoader.Load(folder);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    public static (double Airspeed, double Throttle) Cruise(string id) => id switch
    {
        "trainer" => (15, 0.65),
        "sport" => (18, 0.6),
        "wing" => (14, 0.6),
        "3d" => (14, 0.45),
        _ => throw new ArgumentException(id),
    };

    public static Simulation InFlight(string id, double altitude, double airspeed, double pitchDeg = 0, double rollDeg = 0) =>
        InFlight(Load(id), altitude, airspeed, pitchDeg, rollDeg);

    public static Simulation InFlight(AircraftDefinition def, double altitude, double airspeed, double pitchDeg = 0, double rollDeg = 0)
    {
        var sim = new Simulation(new Aircraft(def), FlightEnvironment.Calm());
        sim.Reset(InitialConditions.InFlight(new Vec3(0, 0, altitude), 0, airspeed, pitchDeg, rollDeg));
        return sim;
    }

    public static Simulation OnGround(string id)
    {
        var def = Load(id);
        var sim = new Simulation(new Aircraft(def), FlightEnvironment.Calm());
        sim.Reset(InitialConditions.OnGround(def, sim.Environment.Terrain, 0, 0, 0));
        return sim;
    }

    public static void Fly(Simulation sim, double seconds, Func<double, ControlInputs> pilot, Action<Simulation>? observe = null)
    {
        int steps = (int)Math.Round(seconds / Simulation.FixedStep);
        for (int i = 0; i < steps; i++)
        {
            sim.StepOnce(pilot(sim.Time));
            observe?.Invoke(sim);
        }
    }

    public static double Roll(Simulation sim) => Attitude.FromOrientation(sim.Aircraft.State.Orientation).Roll;

    public static double WorldYawRightRate(Simulation sim)
    {
        var s = sim.Aircraft.State;
        return -s.Orientation.Rotate(s.AngularVelocity).Z;
    }
}
