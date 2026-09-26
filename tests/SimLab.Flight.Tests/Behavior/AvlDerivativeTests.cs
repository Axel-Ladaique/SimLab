using System.Diagnostics;
using System.Text.Json;
using SimLab.Flight.Aero;
using SimLab.Flight.Geometry;
using Xunit.Abstractions;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>
/// The fleet's stability derivatives against AVL (docs/superpowers/specs/2026-09-26-lifting-line-design.md, success
/// criteria 1–4 and 6). AVL values: Golden/avl-derivatives.json, regenerated with
/// docs/investigations/2026-09-26-avl-comparison/compare_avl.py --write-fixture when an aircraft's geometry changes.
/// </summary>
/// <remarks>
/// Runs alone, after the parallel tests (<see cref="TimingCollection"/>): <see cref="Aero_evaluation_is_cheap"/> times the
/// solver, and the rest of the suite running at the same time would triple its measurement.
/// </remarks>
[Collection(TimingCollection.Name)]
public class AvlDerivativeTests(ITestOutputHelper output)
{
    static readonly string FixturePath = Path.Combine(Fleet.RepoRoot, "tests", "SimLab.Flight.Tests", "Behavior", "Golden", "avl-derivatives.json");

    /// <summary>
    /// Derivatives held to ±15 % of AVL (criterion 1). The control derivatives are compared without SimLab's viscous
    /// <see cref="SurfaceAeroModel.FlapEfficiency"/>, which inviscid AVL does not have.
    /// </summary>
    static readonly string[] Relative = ["CLa", "CYb", "Cnb", "Clb", "Clp", "Cnr", "Clr", "Cl_aileron", "Cm_elevator", "Cn_rudder"];

    static readonly string[] Controls = ["Cl_aileron", "Cm_elevator", "Cn_rudder"];

    /// <summary>
    /// Known gaps of the single-panel lifting line, held to a wider ratio so they cannot grow unnoticed
    /// (docs/realism-backlog.md #16): the flying wing's winglet-on-wing interaction (Clb, Clr) and the trainer's fin.
    /// </summary>
    static readonly Dictionary<(string Id, string Key), double> KnownGaps = new()
    {
        [("wing", "Clb")] = 1.30,
        [("wing", "Clr")] = 1.35,
        [("trainer", "Cnb")] = 1.20,
    };

    /// <summary>
    /// The lifting line puts the neutral point about 3 % of the chord further aft than AVL's ten chordwise panels, whatever
    /// the sweep, plus about 1.6 % with the flying wing's winglets: SimLab's static margin may exceed AVL's by this much
    /// (the safe side), and fall below it by at most 2 %.
    /// </summary>
    const double MarginAboveAvl = 0.065, MarginBelowAvl = 0.02;

    /// <summary>Adverse yaw from the ailerons (/deg): SimLab's is smaller than AVL's by up to this much.</summary>
    const double AileronYawTolerance = 0.0007;

    static readonly string[] Reported = ["CLa", "Cma", "CYb", "Clb", "Cnb", "Clp", "Cnp", "Clr", "Cnr", "CYr", "CLq", "Cmq",
        "Cl_aileron", "Cn_aileron", "Cm_elevator", "CL_elevator", "Cn_rudder", "Cl_rudder"];

    static JsonElement Avl(string id) =>
        JsonDocument.Parse(File.ReadAllText(FixturePath)).RootElement.GetProperty("aircraft").GetProperty(id);

    static Dictionary<string, double> Sim(string id, JsonElement avl)
    {
        var def = Fleet.Load(id);
        var aero = new SurfaceAeroModel(def.Surfaces, def.Airfoils, def.Controls, []);
        return StabilityDerivatives.Compute(aero, StabilityDerivatives.ReferenceOf(def), avl.GetProperty("airspeed").GetDouble(),
            Angle.Rad(avl.GetProperty("alphaDeg").GetDouble()), def.Controls);
    }

    static double Spiral(Func<string, double> d) => d("Clb") * d("Cnr") / (d("Clr") * d("Cnb"));

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    public void Derivatives_match_avl(string id)
    {
        var avl = Avl(id);
        var sim = Sim(id, avl);
        double A(string key) => avl.GetProperty(key).GetDouble();
        var failures = new List<string>();

        foreach (var key in Relative)
        {
            if (!avl.TryGetProperty(key, out var value) || !sim.TryGetValue(key, out double s)) continue;
            if (Controls.Contains(key)) s /= SurfaceAeroModel.FlapEfficiency;
            double ratio = s / value.GetDouble();
            double max = KnownGaps.TryGetValue((id, key), out double gap) ? gap : 1.15;
            if (ratio < 0.85 || ratio > max) failures.Add($"{key} {s:F4} vs AVL {value.GetDouble():F4} (x{ratio:F2})");
        }

        double marginSim = -sim["Cma"] / sim["CLa"], marginAvl = -A("Cma") / A("CLa");
        if (marginSim - marginAvl > MarginAboveAvl || marginAvl - marginSim > MarginBelowAvl) failures.Add($"static margin {marginSim:P1} vs AVL {marginAvl:P1}");

        double spiralSim = Spiral(k => sim[k]), spiralAvl = Spiral(A);
        if ((spiralSim > 1) != (spiralAvl > 1)) failures.Add($"spiral criterion {spiralSim:F2} vs AVL {spiralAvl:F2}");

        if (Math.Sign(sim["Cnp"]) != Math.Sign(A("Cnp")) || Math.Abs(sim["Cnp"] - A("Cnp")) > 0.015)
            failures.Add($"Cnp {sim["Cnp"]:F4} vs AVL {A("Cnp"):F4}");
        if (avl.TryGetProperty("Cn_aileron", out var cnda) && sim.TryGetValue("Cn_aileron", out double cndaSim)
            && Math.Abs(cndaSim - cnda.GetDouble()) > AileronYawTolerance)
            failures.Add($"Cn_aileron {cndaSim:F5} vs AVL {cnda.GetDouble():F5}");

        output.WriteLine($"{id}: static margin {marginSim:P1} (AVL {marginAvl:P1}), spiral {spiralSim:F2} (AVL {spiralAvl:F2})");
        Assert.True(failures.Count == 0, $"{id}: " + string.Join("; ", failures));
    }

    [Fact]
    public void Report_ratio_table()
    {
        foreach (var id in new[] { "trainer", "sport", "wing", "3d" })
        {
            var avl = Avl(id);
            var sim = Sim(id, avl);
            output.WriteLine($"== {id} (V {avl.GetProperty("airspeed").GetDouble()} m/s, alpha {avl.GetProperty("alphaDeg").GetDouble():F2} deg)");
            foreach (var key in Reported)
            {
                if (!avl.TryGetProperty(key, out var a) || !sim.TryGetValue(key, out double s)) continue;
                output.WriteLine($"  {key,-12} SimLab {s,10:F4}  AVL {a.GetDouble(),10:F4}  ratio {s / a.GetDouble(),6:F2}");
            }
        }
    }

    /// <summary>
    /// Criterion 6: under 100 µs per evaluation (20 % of real time at 500 Hz with four RK4 evaluations per step) in a
    /// Release build, asserted there (dotnet test tests/SimLab.Flight.Tests -c Release --filter FullyQualifiedName~Aero_evaluation_is_cheap).
    /// A Debug build only prints the timing: its unoptimised JIT, under the parallel test suite, takes 1.2–2.5 ms per
    /// evaluation, which is load noise, not a regression signal.
    /// </summary>
    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    public void Aero_evaluation_is_cheap(string id)
    {
        var def = Fleet.Load(id);
        var aero = new SurfaceAeroModel(def.Surfaces, def.Airfoils, def.Controls, def.Bodies);
        var deflections = new double[def.Controls.Count];
        double speed = Fleet.Cruise(id).Airspeed;
        AeroContext Context(int i)
        {
            double alpha = Angle.Rad(3 + 0.01 * (i % 10));
            return new AeroContext(new Vec3(-speed * Math.Cos(alpha), 0, -speed * Math.Sin(alpha)), new Vec3(0.1, 0.05, 0.02),
                1.225, 50, Vec3.UnitZ, deflections, default);
        }
        for (int i = 0; i < 500; i++) aero.Evaluate(Context(i));
        const int count = 4000;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < count; i++) aero.Evaluate(Context(i));
        double micro = watch.Elapsed.TotalMilliseconds * 1000 / count;
        output.WriteLine($"{id}: {aero.Segments.Count} strips, {micro:F1} µs per evaluation, last solve {aero.Line.LastIterations} iterations, " +
                         $"{aero.Line.NonConvergedSolves} non-converged");
#if !DEBUG
        Assert.True(micro < 100, $"{micro:F1} µs per evaluation");
#endif
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public class TimingCollection
{
    public const string Name = "Timing";
}
