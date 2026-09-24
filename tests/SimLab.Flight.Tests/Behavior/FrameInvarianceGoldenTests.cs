using System.Globalization;
using System.Text.Json;
using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Sim;
using SimLab.Flight.Terrain;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>
/// Frame-invariant metrics of scripted flights, frozen before the axis-convention refactor.
/// Regenerate ONLY with SIMLAB_WRITE_GOLDEN=1 and only when a physics change is intended.
/// </summary>
public class FrameInvarianceGoldenTests
{
    public sealed record Snapshot(
        double T, double Airspeed, double East, double North, double Up,
        double HeadingDeg, double PitchDeg, double RollDeg, double AlphaDeg, double BetaDeg,
        double RollRightRate, double PitchUpRate, double YawRightRate, double Rpm, string Crash);

    static readonly string GoldenPath = Path.Combine(Fleet.RepoRoot, "tests", "SimLab.Flight.Tests", "Behavior", "Golden", "frame-invariance.json");

    public static IEnumerable<object[]> Scenarios() =>
        new[] { "trainer_takeoff", "sport_rolls", "wing_launch", "trainer_wind" }.Select(s => new object[] { s });

    static (Simulation Sim, double Seconds, Func<double, ControlInputs> Pilot) Build(string scenario)
    {
        switch (scenario)
        {
            case "trainer_takeoff":
            {
                var def = Fleet.Load("trainer");
                var sim = new Simulation(new Aircraft(def), FlightEnvironment.Calm());
                sim.Reset(InitialConditions.OnGround(def, sim.Environment.Terrain, 12, -7, 130));
                return (sim, 8, t => new ControlInputs(1, 0, t > 3 ? 0.3 : 0, 0.1));
            }
            case "sport_rolls":
            {
                var sim = new Simulation(new Aircraft(Fleet.Load("sport")), FlightEnvironment.Calm());
                sim.Reset(InitialConditions.InFlight(new Vec3(5, 80, -3), 30, 18, pitchDeg: 3, rollDeg: -10));
                return (sim, 5, t => new ControlInputs(0.6, t is > 1 and < 2 ? 0.5 : 0, t is > 2.5 and < 3 ? 0.3 : 0, t is > 3 and < 4 ? 0.4 : 0));
            }
            case "wing_launch":
            {
                var sim = new Simulation(new Aircraft(Fleet.Load("wing")), FlightEnvironment.Calm());
                sim.Reset(InitialConditions.InFlight(new Vec3(0, 1.8, 22), 250, 10, pitchDeg: 10));
                return (sim, 5, _ => new ControlInputs(1, 0, 0.1, 0));
            }
            case "trainer_wind":
            {
                var env = new FlightEnvironment(new FlatTerrain(), new WindField(new WindSettings(SpeedAt10m: 5, FromDirectionDeg: 60, Turbulence: 1), seed: 3));
                var sim = new Simulation(new Aircraft(Fleet.Load("trainer")), env);
                sim.Reset(InitialConditions.InFlight(new Vec3(-20, 60, 10), 200, 16));
                return (sim, 6, t => new ControlInputs(0.65, t is > 2 and < 2.5 ? -0.4 : 0, 0.05, 0));
            }
            default: throw new ArgumentException(scenario);
        }
    }

    static List<Snapshot> Run(string scenario)
    {
        var (sim, seconds, pilot) = Build(scenario);
        var snapshots = new List<Snapshot>();
        int steps = (int)Math.Round(seconds / Simulation.FixedStep);
        int every = (int)Math.Round(0.5 / Simulation.FixedStep);
        for (int i = 1; i <= steps; i++)
        {
            sim.StepOnce(pilot(sim.Time));
            if (i % every != 0) continue;
            var a = sim.Aircraft;
            var s = a.State;
            var att = Attitude.FromOrientation(s.Orientation);
            snapshots.Add(new Snapshot(
                Math.Round(sim.Time, 6), a.AirData.Airspeed,
                PilotFrame.East(s.Position), PilotFrame.North(s.Position), PilotFrame.Up(s.Position),
                Angle.Deg(att.Heading), Angle.Deg(att.Pitch), Angle.Deg(att.Roll),
                Angle.Deg(a.AirData.Alpha), Angle.Deg(a.AirData.Beta),
                PilotFrame.RollRightRate(s.AngularVelocity), PilotFrame.PitchUpRate(s.AngularVelocity), PilotFrame.YawRightRate(s.AngularVelocity),
                a.Power?.Telemetry.Rpm ?? 0, a.Crash.ToString()));
        }
        return snapshots;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Flight_metrics_match_the_golden_file(string scenario)
    {
        var actual = Run(scenario);
        if (Environment.GetEnvironmentVariable("SIMLAB_WRITE_GOLDEN") == "1")
        {
            var all = File.Exists(GoldenPath)
                ? JsonSerializer.Deserialize<Dictionary<string, List<Snapshot>>>(File.ReadAllText(GoldenPath))!
                : new Dictionary<string, List<Snapshot>>();
            all[scenario] = actual;
            Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath)!);
            File.WriteAllText(GoldenPath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        var golden = JsonSerializer.Deserialize<Dictionary<string, List<Snapshot>>>(File.ReadAllText(GoldenPath))![scenario];
        Assert.Equal(golden.Count, actual.Count);
        for (int i = 0; i < golden.Count; i++)
        {
            var g = golden[i];
            var a = actual[i];
            string at = $"{scenario} t={g.T.ToString(CultureInfo.InvariantCulture)}";
            Assert.Equal(g.Crash, a.Crash);
            Near(g.Airspeed, a.Airspeed, 1e-4, at + " airspeed");
            Near(g.East, a.East, 1e-4, at + " east");
            Near(g.North, a.North, 1e-4, at + " north");
            Near(g.Up, a.Up, 1e-4, at + " up");
            NearAngle(g.HeadingDeg, a.HeadingDeg, 1e-3, at + " heading");
            Near(g.PitchDeg, a.PitchDeg, 1e-3, at + " pitch");
            NearAngle(g.RollDeg, a.RollDeg, 1e-3, at + " roll");
            Near(g.AlphaDeg, a.AlphaDeg, 1e-3, at + " alpha");
            Near(g.BetaDeg, a.BetaDeg, 1e-3, at + " beta");
            Near(g.RollRightRate, a.RollRightRate, 1e-5, at + " roll rate");
            Near(g.PitchUpRate, a.PitchUpRate, 1e-5, at + " pitch rate");
            Near(g.YawRightRate, a.YawRightRate, 1e-5, at + " yaw rate");
            Near(g.Rpm, a.Rpm, 1e-2, at + " rpm");
        }
    }

    static void Near(double expected, double actual, double tolerance, string what) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what}: expected {expected}, got {actual}");

    static void NearAngle(double expected, double actual, double tolerance, string what) =>
        Assert.True(Math.Abs(Math.IEEERemainder(expected - actual, 360)) <= tolerance, $"{what}: expected {expected}, got {actual}");
}
