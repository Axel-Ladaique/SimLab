namespace SimLab.Flight.Aero;

public static class InducedFlow
{
    /// <summary>McCormick ground-effect factor on induced flow: (16h/b)² / (1 + (16h/b)²).</summary>
    public static double GroundEffectFactor(double height, double span)
    {
        if (span <= 0) return 1.0;
        double x = 16 * Math.Max(height, 0) / span;
        return x * x / (1 + x * x);
    }
}
