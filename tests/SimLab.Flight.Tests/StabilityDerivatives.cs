using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests;

/// <summary>
/// Stability-axis derivatives of an aero model by central differences, in AVL's conventions: Cl' &gt; 0 right wing down,
/// Cm &gt; 0 nose up, Cn' &gt; 0 nose right; β &gt; 0 wind from the right; rates per p b/2V, q c/2V, r b/2V; controls per
/// degree of channel with each surface deflected by its mix weight. Sea level, far from the ground, downwash lag settled,
/// moments about the model origin (the CG). Same harness as docs/investigations/2026-09-26-avl-comparison/Deriv.cs.
/// </summary>
internal static class StabilityDerivatives
{
    public sealed record Reference(double Area, double Span, double Chord);

    public static readonly string[] Names = ["CL", "CD", "CY", "Cl", "Cm", "Cn"];

    /// <summary>Wing area and span, and the first wing surface's mean aerodynamic chord (AVL's Sref, Bref, Cref from Waxwing).</summary>
    public static Reference ReferenceOf(AircraftDefinition def)
    {
        var wings = def.Surfaces.Where(s => s.Role == SurfaceRole.Wing).ToArray();
        var first = wings[0];
        double taper = first.TipChord / first.RootChord;
        double mac = 2.0 / 3.0 * first.RootChord * (1 + taper + taper * taper) / (1 + taper);
        return new Reference(wings.Sum(s => s.TotalArea), wings.Max(s => s.TotalSpan), mac);
    }

    /// <summary>[CL, CD, CY, Cl, Cm, Cn] at this condition.</summary>
    public static double[] Evaluate(SurfaceAeroModel aero, Reference r, double airspeed, double alpha, IReadOnlyList<double> deflections,
        double beta = 0, double pHat = 0, double qHat = 0, double rHat = 0)
    {
        var air = new Vec3(-airspeed * Math.Cos(alpha) * Math.Cos(beta), airspeed * Math.Sin(beta), -airspeed * Math.Sin(alpha) * Math.Cos(beta));
        double p = pHat * 2 * airspeed / r.Span, q = qHat * 2 * airspeed / r.Chord, rate = rHat * 2 * airspeed / r.Span;
        // Stability-axis rates → body FRD → SimLab body axes (x back, y right, z up).
        double pf = p * Math.Cos(alpha) - rate * Math.Sin(alpha), rf = p * Math.Sin(alpha) + rate * Math.Cos(alpha);
        var ctx = new AeroContext(air, new Vec3(-pf, q, -rf), Isa.SeaLevelDensity, 1000, Vec3.UnitZ, deflections, default);
        aero.Reset();
        for (int i = 0; i < 3; i++)
        {
            aero.Evaluate(ctx);
            aero.Advance(100); // the tail's downwash straight to its steady value
        }
        var load = aero.Evaluate(ctx);
        // SimLab body → FRD → stability axes.
        var f = new Vec3(-load.Force.X, load.Force.Y, -load.Force.Z);
        var m = new Vec3(-load.Moment.X, load.Moment.Y, -load.Moment.Z);
        double ca = Math.Cos(alpha), sa = Math.Sin(alpha);
        double xs = f.X * ca + f.Z * sa, zs = -f.X * sa + f.Z * ca;
        double ls = m.X * ca + m.Z * sa, ns = -m.X * sa + m.Z * ca;
        double qS = 0.5 * Isa.SeaLevelDensity * airspeed * airspeed * r.Area;
        return [-zs / qS, -xs / qS, f.Y / qS, ls / (qS * r.Span), m.Y / (qS * r.Chord), ns / (qS * r.Span)];
    }

    public static Dictionary<string, double> Compute(SurfaceAeroModel aero, Reference r, double airspeed, double alpha,
        IReadOnlyList<ControlSurfaceSpec> controls)
    {
        var zero = new double[controls.Count];
        var d = new Dictionary<string, double>();
        var basis = Evaluate(aero, r, airspeed, alpha, zero);
        for (int k = 0; k < Names.Length; k++) d[Names[k]] = basis[k];

        void Central(string suffix, Func<double, double[]> at, double h)
        {
            var up = at(h);
            var down = at(-h);
            for (int k = 0; k < Names.Length; k++) d[Names[k] + suffix] = (up[k] - down[k]) / (2 * h);
        }
        double da = Angle.Rad(0.5);
        Central("a", h => Evaluate(aero, r, airspeed, alpha + h, zero), da);
        Central("b", h => Evaluate(aero, r, airspeed, alpha, zero, beta: h), da);
        Central("p", h => Evaluate(aero, r, airspeed, alpha, zero, pHat: h), 0.01);
        Central("q", h => Evaluate(aero, r, airspeed, alpha, zero, qHat: h), 0.01);
        Central("r", h => Evaluate(aero, r, airspeed, alpha, zero, rHat: h), 0.01);
        foreach (var channel in new[] { "aileron", "elevator", "rudder" })
        {
            var weights = controls.Select(c => c.Mix.TryGetValue(channel, out var w) ? w : 0).ToArray();
            if (weights.All(w => w == 0)) continue;
            Central("_" + channel, h => Evaluate(aero, r, airspeed, alpha, weights.Select(w => w * Angle.Rad(h)).ToArray()), 1.0);
        }
        return d;
    }
}
