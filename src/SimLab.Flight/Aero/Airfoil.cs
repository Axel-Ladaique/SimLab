using SimLab.Flight.Geometry;
using SimLab.Flight.Numerics;

namespace SimLab.Flight.Aero;

public sealed record AirfoilTable(double Reynolds, double[] AlphaDeg, double[] Cl, double[] Cd, double[] Cm);

public readonly record struct AirfoilCoefficients(double Cl, double Cd, double Cm)
{
    public static AirfoilCoefficients Lerp(AirfoilCoefficients a, AirfoilCoefficients b, double t) =>
        new(a.Cl + (b.Cl - a.Cl) * t, a.Cd + (b.Cd - a.Cd) * t, a.Cm + (b.Cm - a.Cm) * t);
}

/// <summary>
/// 2D airfoil polars over one or more Reynolds numbers. Outside the tabulated alpha range the
/// coefficients blend smoothly (over <see cref="BlendWidthRad"/>) into a flat-plate model valid to ±180°.
/// </summary>
public sealed class Airfoil
{
    public const double FlatPlateNormalCoefficient = 1.98;
    public static readonly double BlendWidthRad = Angle.Rad(8);

    sealed record Prepared(double Reynolds, double[] Alpha, double[] Cl, double[] Cd, double[] Cm, double Cd0);

    readonly Prepared[] _tables;

    public Airfoil(string name, IEnumerable<AirfoilTable> tables)
    {
        Name = name;
        _tables = tables.OrderBy(t => t.Reynolds).Select(Prepare).ToArray();
        if (_tables.Length == 0) throw new ArgumentException($"Airfoil '{name}' has no tables.");
    }

    public string Name { get; }

    public AirfoilCoefficients Evaluate(double alphaRad, double reynolds)
    {
        double a = Math.IEEERemainder(alphaRad, 2 * Math.PI);
        if (_tables.Length == 1 || reynolds <= _tables[0].Reynolds) return Sample(_tables[0], a);
        var last = _tables[^1];
        if (reynolds >= last.Reynolds) return Sample(last, a);
        int i = 0;
        while (_tables[i + 1].Reynolds < reynolds) i++;
        var lo = _tables[i];
        var hi = _tables[i + 1];
        double t = (reynolds - lo.Reynolds) / (hi.Reynolds - lo.Reynolds);
        return AirfoilCoefficients.Lerp(Sample(lo, a), Sample(hi, a), t);
    }

    public static AirfoilCoefficients FlatPlate(double alphaRad, double cd0)
    {
        double cn = FlatPlateNormalCoefficient * Math.Sin(alphaRad);
        double centerOfPressureArm = 0.25 - 0.175 * (1 - 2 * Math.Abs(alphaRad) / Math.PI);
        return new AirfoilCoefficients(cn * Math.Cos(alphaRad), cd0 + cn * Math.Sin(alphaRad), -cn * centerOfPressureArm);
    }

    static AirfoilCoefficients Sample(Prepared t, double a)
    {
        double lo = t.Alpha[0], hi = t.Alpha[^1];
        double clamped = Math.Clamp(a, lo, hi);
        var attached = new AirfoilCoefficients(
            Interpolation.Linear(t.Alpha, t.Cl, clamped),
            Interpolation.Linear(t.Alpha, t.Cd, clamped),
            Interpolation.Linear(t.Alpha, t.Cm, clamped));
        double excess = a > hi ? a - hi : a < lo ? lo - a : 0;
        if (excess <= 0) return attached;
        return AirfoilCoefficients.Lerp(attached, FlatPlate(a, t.Cd0), SmoothStep(excess / BlendWidthRad));
    }

    static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    static Prepared Prepare(AirfoilTable t)
    {
        int n = t.AlphaDeg.Length;
        if (n < 2 || t.Cl.Length != n || t.Cd.Length != n || t.Cm.Length != n)
            throw new ArgumentException("Airfoil table arrays must all have the same length (at least 2).");
        Interpolation.RequireIncreasing(t.AlphaDeg, "alphaDeg");
        return new Prepared(t.Reynolds, t.AlphaDeg.Select(Angle.Rad).ToArray(), t.Cl, t.Cd, t.Cm, t.Cd.Min());
    }
}
