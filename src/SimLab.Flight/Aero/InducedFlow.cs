namespace SimLab.Flight.Aero;

public static class InducedFlow
{
    /// <summary>
    /// Solves α_i = k · Cl(α_geo − α_i) with a relaxed fixed-point iteration,
    /// where k = 1 / (π e AR), optionally reduced by ground effect.
    /// </summary>
    public static double SolveInducedAngle(Airfoil airfoil, double reynolds, double alphaGeo, double inducedFactor)
    {
        if (inducedFactor <= 0) return 0;
        double ai = 0;
        for (int i = 0; i < 6; i++)
        {
            double target = inducedFactor * airfoil.Evaluate(alphaGeo - ai, reynolds).Cl;
            ai = 0.5 * (ai + target);
        }
        return ai;
    }

    /// <summary>McCormick ground-effect factor on induced flow: (16h/b)² / (1 + (16h/b)²).</summary>
    public static double GroundEffectFactor(double height, double span)
    {
        if (span <= 0) return 1.0;
        double x = 16 * Math.Max(height, 0) / span;
        return x * x / (1 + x * x);
    }
}
