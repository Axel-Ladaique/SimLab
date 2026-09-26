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
public class AvlDerivativeTests(ITestOutputHelper output)
{
    static readonly string FixturePath = Path.Combine(Fleet.RepoRoot, "tests", "SimLab.Flight.Tests", "Behavior", "Golden", "avl-derivatives.json");

    /// <summary>Derivatives held to ±15 % of AVL (criterion 1).</summary>
    static readonly string[] Relative = ["CLa", "CYb", "Cnb", "Clb", "Clp", "Cnr", "Clr", "Cl_aileron", "Cm_elevator", "Cn_rudder"];

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
            double ratio = s / value.GetDouble();
            if (ratio is < 0.85 or > 1.15) failures.Add($"{key} {s:F4} vs AVL {value.GetDouble():F4} (x{ratio:F2})");
        }

        double marginSim = -sim["Cma"] / sim["CLa"], marginAvl = -A("Cma") / A("CLa");
        if (Math.Abs(marginSim - marginAvl) > 0.02) failures.Add($"static margin {marginSim:P1} vs AVL {marginAvl:P1}");

        double spiralSim = Spiral(k => sim[k]), spiralAvl = Spiral(A);
        if ((spiralSim > 1) != (spiralAvl > 1)) failures.Add($"spiral criterion {spiralSim:F2} vs AVL {spiralAvl:F2}");

        if (Math.Sign(sim["Cnp"]) != Math.Sign(A("Cnp")) || Math.Abs(sim["Cnp"] - A("Cnp")) > 0.015)
            failures.Add($"Cnp {sim["Cnp"]:F4} vs AVL {A("Cnp"):F4}");
        if (avl.TryGetProperty("Cn_aileron", out var cnda) && sim.TryGetValue("Cn_aileron", out double cndaSim)
            && Math.Abs(cndaSim - cnda.GetDouble()) > 0.0003)
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
    /// Criterion 6: under 50 µs per evaluation in a Release build, asserted there
    /// (dotnet test tests/SimLab.Flight.Tests -c Release --filter FullyQualifiedName~Aero_evaluation_is_cheap). A Debug
    /// build (unoptimised JIT) only gets a 1000 µs guard against an algorithmic regression.
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
#if DEBUG
        Assert.True(micro < 1000, $"{micro:F1} µs per evaluation (Debug guard)");
#else
        Assert.True(micro < 50, $"{micro:F1} µs per evaluation");
#endif
    }
}
