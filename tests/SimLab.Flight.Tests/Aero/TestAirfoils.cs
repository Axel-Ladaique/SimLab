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

    /// <summary>
    /// A low-Reynolds laminar-bubble kink: lift slope 4π/rad within ±1°, 2π/rad outside it (continuous), Cd 0.01, Cm 0.
    /// </summary>
    public static Airfoil Kinked()
    {
        double[] alpha = [-12, -1, 1, 12];
        double Cl(double a) => Math.Abs(a) <= 1 ? 4 * Math.PI * Angle(a) : Math.Sign(a) * (4 * Math.PI * Angle(1) + 2 * Math.PI * Angle(Math.Abs(a) - 1));
        static double Angle(double deg) => deg * Math.PI / 180;
        return new Airfoil("kinked", [new AirfoilTable(
            200_000, alpha,
            alpha.Select(Cl).ToArray(),
            alpha.Select(_ => 0.01).ToArray(),
            alpha.Select(_ => 0.0).ToArray())]);
    }

    public static IReadOnlyDictionary<string, Airfoil> Map() => new Dictionary<string, Airfoil> { ["linear"] = Linear() };
}
