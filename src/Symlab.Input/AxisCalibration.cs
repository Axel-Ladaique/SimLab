namespace Symlab.Input;

/// <summary>Raw axis limits captured by the calibration wizard. Each half is scaled separately.</summary>
public sealed record AxisCalibration(double Min, double Center, double Max)
{
    public static readonly AxisCalibration Identity = new(-1, 0, 1);

    public double Normalize(double raw)
    {
        if (raw >= Center)
        {
            double span = Max - Center;
            return span <= 1e-9 ? 0 : Math.Clamp((raw - Center) / span, 0, 1);
        }
        double spanLow = Center - Min;
        return spanLow <= 1e-9 ? 0 : Math.Clamp((raw - Center) / spanLow, -1, 0);
    }
}
