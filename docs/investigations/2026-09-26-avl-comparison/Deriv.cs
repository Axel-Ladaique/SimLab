using System.Text.Json;
using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Controls;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

// Usage: Deriv <aircraft dir> <airspeed m/s> [alphaDeg]
// Stability-axis derivatives (AVL conventions: Cl' > 0 right wing down, Cm > 0 nose up, Cn' > 0 nose right,
// beta > 0 wind from the right; rates per nondimensional p b/2V, q c/2V, r b/2V; controls per degree of channel).
var def = AircraftLoader.Load(args[0]);
double V = double.Parse(args[1]);
const double rho = 1.225;
var wing = def.Surfaces.Where(s => s.Role == SurfaceRole.Wing).ToArray();
double S = wing.Sum(s => s.TotalArea), b = wing.Max(s => s.TotalSpan);
var w0 = wing[0];
double taper = w0.TipChord / w0.RootChord;
double c = 2.0 / 3.0 * w0.RootChord * (1 + taper + taper * taper) / (1 + taper);
double qS = 0.5 * rho * V * V * S;
double weight = def.Mass.Mass * Aircraft.Gravity;

var result = new Dictionary<string, object>();
foreach (var (label, withBodies) in new[] { ("surfaces", false), ("withBodies", true) })
{
    var aero = new SurfaceAeroModel(def.Surfaces, def.Airfoils, def.Controls, withBodies ? def.Bodies : []);
    double[] zero = new double[def.Controls.Count];

    double[] Coeffs(double alpha, double beta, double ph, double qh, double rh, double[] defl)
    {
        var air = new Vec3(-V * Math.Cos(alpha) * Math.Cos(beta), V * Math.Sin(beta), -V * Math.Sin(alpha) * Math.Cos(beta));
        double p = ph * 2 * V / b, q = qh * 2 * V / c, r = rh * 2 * V / b;
        // Stability-axis rates -> body FRD -> SimLab body (x back, y right, z up).
        double pf = p * Math.Cos(alpha) - r * Math.Sin(alpha), rf = p * Math.Sin(alpha) + r * Math.Cos(alpha);
        var omega = new Vec3(-pf, q, -rf);
        aero.Reset();
        BodyLoad load = default;
        for (int i = 0; i < 5; i++)
        {
            load = aero.Evaluate(new AeroContext(air, omega, rho, 1000, Vec3.UnitZ, defl, default));
            aero.Advance(100); // downwash straight to its quasi-steady value
        }
        load = aero.Evaluate(new AeroContext(air, omega, rho, 1000, Vec3.UnitZ, defl, default));
        // SimLab body -> FRD -> stability axes.
        var F = new Vec3(-load.Force.X, load.Force.Y, -load.Force.Z);
        var M = new Vec3(-load.Moment.X, load.Moment.Y, -load.Moment.Z);
        double ca = Math.Cos(alpha), sa = Math.Sin(alpha);
        double Xs = F.X * ca + F.Z * sa, Zs = -F.X * sa + F.Z * ca;
        double Ls = M.X * ca + M.Z * sa, Ns = -M.X * sa + M.Z * ca;
        return [-Zs / qS, -Xs / qS, F.Y / qS, Ls / (qS * b), M.Y / (qS * c), Ns / (qS * b)];
    }
    string[] names = ["CL", "CD", "CY", "Cl", "Cm", "Cn"];

    double alpha0;
    if (args.Length > 2) alpha0 = Angle.Rad(double.Parse(args[2]));
    else
    {
        double lo = Angle.Rad(-6), hi = Angle.Rad(12);
        for (int i = 0; i < 60; i++) { double m = 0.5 * (lo + hi); if (Coeffs(m, 0, 0, 0, 0, zero)[0] * qS < weight) lo = m; else hi = m; }
        alpha0 = 0.5 * (lo + hi);
    }

    var entry = new Dictionary<string, double>();
    var basis = Coeffs(alpha0, 0, 0, 0, 0, zero);
    for (int k = 0; k < 6; k++) entry[names[k]] = basis[k];
    void D(string suffix, Func<double, double[]> f, double h)
    {
        var up = f(h); var dn = f(-h);
        for (int k = 0; k < 6; k++) entry[names[k] + suffix] = (up[k] - dn[k]) / (2 * h);
    }
    double da = Angle.Rad(0.5);
    D("a", h => Coeffs(alpha0 + h, 0, 0, 0, 0, zero), da);
    D("b", h => Coeffs(alpha0, h, 0, 0, 0, zero), da);
    D("p", h => Coeffs(alpha0, 0, h, 0, 0, zero), 0.01);
    D("q", h => Coeffs(alpha0, 0, 0, h, 0, zero), 0.01);
    D("r", h => Coeffs(alpha0, 0, 0, 0, h, zero), 0.01);
    foreach (var ch in new[] { "aileron", "elevator", "rudder" })
    {
        var weights = def.Controls.Select(ctl => ctl.Mix.TryGetValue(ch, out var w) ? w : 0).ToArray();
        if (weights.All(w => w == 0)) continue;
        // AVL control variable: surface deflection = mix weight × channel degrees.
        D("_" + ch, h => Coeffs(alpha0, 0, 0, 0, 0, weights.Select(w => w * Angle.Rad(h)).ToArray()), 1.0);
    }
    entry["alphaDeg"] = Angle.Deg(alpha0);
    result[label] = entry;
}
result["ref"] = new Dictionary<string, double> { ["Sref"] = S, ["Bref"] = b, ["Cref"] = c, ["V"] = V };
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
