using SimLab.Flight.Aero;

namespace SimLab.Flight.Tests.Aero;

internal static class TestAirfoils
{
    /// <summary>Thin-airfoil lift slope (2π/rad) between -10° and +10°, constant Cd 0.01, Cm 0.</summary>
    public static Airfoil Linear()
    {
        double[] alpha = [-10, -5, 0, 5, 10];
        return new Airfoil("linear", [new AirfoilTable(
            200_000, alpha,
            alpha.Select(a => 2 * Math.PI * a * Math.PI / 180).ToArray(),
            alpha.Select(_ => 0.01).ToArray(),
            alpha.Select(_ => 0.0).ToArray())]);
    }

    public static IReadOnlyDictionary<string, Airfoil> Map() => new Dictionary<string, Airfoil> { ["linear"] = Linear() };
}
