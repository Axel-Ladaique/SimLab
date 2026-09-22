using System.Globalization;

namespace Symlab.Flight.Propulsion;

public readonly record struct ThrustStandPoint(double Throttle, double ThrustN, double CurrentA);

/// <summary>Recalibrates a propulsion model so its static thrust and current match thrust-stand measurements.</summary>
public static class ThrustStand
{
    public static IReadOnlyList<ThrustStandPoint> ParseCsv(string text)
    {
        var points = new List<ThrustStandPoint>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(',');
            if (parts.Length < 3 || !TryParse(parts[0], out var throttle)) continue;
            if (!TryParse(parts[1], out var thrust) || !TryParse(parts[2], out var current))
                throw new InvalidDataException($"Invalid thrust-stand line: '{line}'.");
            if (throttle is < 0 or > 1)
                throw new InvalidDataException($"Throttle must be within 0..1 (line '{line}').");
            points.Add(new ThrustStandPoint(throttle, thrust, current));
        }
        return points;
    }

    public static PowerPlantSpec Calibrate(PowerPlantSpec spec, IReadOnlyList<ThrustStandPoint> points, double density = 1.225)
    {
        var usable = points.Where(p => p.Throttle > 0.05 && p.ThrustN > 0 && p.CurrentA > 0).ToArray();
        if (usable.Length == 0) throw new ArgumentException("Thrust-stand data needs at least one point above 5% throttle.");

        double ctScale = 1, cpScale = 1;
        for (int iteration = 0; iteration < 12; iteration++)
        {
            var plant = new PowerPlant(spec with { Propeller = spec.Propeller.Scaled(ctScale, cpScale) });
            double currentRatio = 0, thrustRatio = 0;
            foreach (var p in usable)
            {
                var s = plant.SteadyState(p.Throttle, 0, density);
                currentRatio += p.CurrentA / Math.Max(s.Current, 1e-3);
                thrustRatio += p.ThrustN / Math.Max(s.Thrust, 1e-3);
            }
            cpScale *= Math.Pow(currentRatio / usable.Length, 1.5);
            ctScale *= thrustRatio / usable.Length;
        }
        return spec with { Propeller = spec.Propeller.Scaled(ctScale, cpScale) };
    }

    static bool TryParse(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
