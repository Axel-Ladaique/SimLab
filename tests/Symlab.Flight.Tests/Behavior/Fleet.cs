using Symlab.Flight.Airframe;
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Sim;

namespace Symlab.Flight.Tests.Behavior;

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

    public static (double Airspeed, double Throttle) Cruise(string id) => id switch
    {
        "trainer" => (15, 0.65),
        "sport" => (18, 0.6),
        "wing" => (14, 0.6),
        _ => throw new ArgumentException(id),
    };

    public static Simulation InFlight(string id, double altitude, double airspeed, double pitchDeg = 0, double rollDeg = 0)
    {
        var sim = new Simulation(new Aircraft(Load(id)), FlightEnvironment.Calm());
        sim.Reset(InitialConditions.InFlight(new Vec3(0, altitude, 0), 0, airspeed, pitchDeg, rollDeg));
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
        return -s.Orientation.Rotate(s.AngularVelocity).Y;
    }
}
