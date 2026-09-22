namespace Symlab.Flight.Numerics;

public static class Interpolation
{
    /// <summary>Piecewise-linear lookup on a strictly increasing axis, clamped to the end values.</summary>
    public static double Linear(double[] xs, double[] ys, double x)
    {
        int n = xs.Length;
        if (n == 0 || n != ys.Length) throw new ArgumentException("Table axes must be non-empty and of equal length.");
        if (x <= xs[0]) return ys[0];
        if (x >= xs[n - 1]) return ys[n - 1];
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (xs[mid] <= x) lo = mid; else hi = mid;
        }
        double t = (x - xs[lo]) / (xs[hi] - xs[lo]);
        return ys[lo] + t * (ys[hi] - ys[lo]);
    }

    public static void RequireIncreasing(double[] xs, string what)
    {
        for (int i = 1; i < xs.Length; i++)
            if (xs[i] <= xs[i - 1]) throw new ArgumentException($"{what} must be strictly increasing.");
    }
}
