using SimLab.Flight.Aero;
using SimLab.Flight.Geometry;
using Xunit.Abstractions;

namespace SimLab.Flight.Tests.Aero;

/// <summary>
/// The lifting line against AVL 3.40 (flat plate, 12 chordwise panels) on simple planforms, with the linear 2π test polar.
/// AVL values from the 2026-09-26 prototype runs (docs/superpowers/specs/2026-09-26-lifting-line-design.md, section 1).
/// </summary>
public class LiftingLineValidationTests(ITestOutputHelper output)
{
    const double Speed = 15;
    static readonly StabilityDerivatives.Reference Ar6 = new(0.375, 1.5, 0.25);
    static readonly SurfaceSpec Rectangular = new("wing", SurfaceRole.Wing, Vec3.Zero, 0.75, 0.25, 0.25, 0, 0, 0, 0, "linear", 12, true);
    static readonly SurfaceSpec Fin = new("fin", SurfaceRole.VerticalTail, new Vec3(0.8, 0, 0.05), 0.2, 0.18, 0.12, 20, 90, 0, 0, "linear", 16, false);

    Dictionary<string, double> Derivatives(StabilityDerivatives.Reference r, params SurfaceSpec[] surfaces)
    {
        var d = StabilityDerivatives.Compute(new SurfaceAeroModel(surfaces, TestAirfoils.Map(), [], []), r, Speed, Angle.Rad(4), []);
        output.WriteLine(string.Join(", ", new[] { "CL", "CD", "CLa", "Clp", "CYb", "Clb", "Cnb" }.Select(k => $"{k} {d[k]:F4}")));
        return d;
    }

    static void Near(double expected, double actual, double fraction, string what) =>
        Assert.True(Math.Abs(actual - expected) <= fraction * Math.Abs(expected),
            $"{what}: {actual:F4}, AVL {expected:F4} (±{fraction:P0})");

    [Fact]
    public void Rectangular_wing_lift_slope_and_roll_damping_match_avl()
    {
        var d = Derivatives(Ar6, Rectangular);
        Near(4.190, d["CLa"], 0.04, "CLα");
        Near(-0.4376, d["Clp"], 0.08, "Clp");
        Near(-0.0372, d["Clb"], 0.10, "Clβ (lift-dependent part, chordwise trailing legs)");
    }

    [Fact]
    public void Lift_slope_converges_with_the_strip_count()
    {
        double coarse = Derivatives(Ar6, Rectangular)["CLa"];
        double fine = Derivatives(Ar6, Rectangular with { Segments = 24 })["CLa"];
        Near(fine, coarse, 0.02, "CLα 12 vs 24 strips");
    }

    [Fact]
    public void Rectangular_wing_span_efficiency_is_close_to_one()
    {
        var d = Derivatives(Ar6, Rectangular);
        double induced = d["CD"] - 0.01; // the test polar's profile drag
        double e = d["CL"] * d["CL"] / (Math.PI * 6 * induced);
        Assert.InRange(e, 0.9, 1.1);
    }

    [Fact]
    public void Dihedral_effect_matches_avl()
    {
        var d = Derivatives(Ar6, Rectangular with { DihedralDeg = 5 });
        Near(-0.1007, d["Clb"], 0.10, "Clβ");
    }

    [Fact]
    public void Fin_behind_a_dihedral_wing_matches_avl_in_sideslip()
    {
        var d = Derivatives(Ar6, Rectangular with { DihedralDeg = 5 }, Fin);
        Near(-0.1579, d["CYb"], 0.10, "CYβ");
        Near(0.0764, d["Cnb"], 0.10, "Cnβ");
        Near(-0.1067, d["Clb"], 0.10, "Clβ");
    }

    [Fact]
    public void Swept_tapered_wing_lift_slope_matches_avl()
    {
        var swept = new SurfaceSpec("wing", SurfaceRole.Wing, new Vec3(0.213, 0, 0), 0.55, 0.30, 0.15, 25, 0, 0, 0, "linear", 8, true);
        var d = Derivatives(new StabilityDerivatives.Reference(0.2475, 1.1, 0.23333), swept);
        Near(3.854, d["CLa"], 0.06, "CLα");
    }
}
