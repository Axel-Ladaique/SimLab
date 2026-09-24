using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>
/// Static longitudinal stability from the aircraft's own aero model (power off, zero rates, out of ground effect):
/// neutral point and static margin by finite differences, and the level-flight trim point.
/// </summary>
internal static class StaticStability
{
    const double FarFromGround = 1000;

    /// <summary>Mean aerodynamic chord of the (trapezoidal) wing, m.</summary>
    public static double MeanAerodynamicChord(AircraftDefinition def)
    {
        var wing = def.Surfaces.Single(s => s.Role == SurfaceRole.Wing);
        double taper = wing.TipChord / wing.RootChord;
        return 2.0 / 3.0 * wing.RootChord * (1 + taper + taper * taper) / (1 + taper);
    }

    /// <summary>Lift (N, perpendicular to the flow) and pitch-up moment about the CG (N·m) at this alpha and elevator command.</summary>
    public static (double Lift, double PitchMoment) Loads(Aircraft aircraft, double airspeed, double alpha, double elevator)
    {
        var controls = aircraft.Definition.Controls;
        var input = new ControlInputs(0, 0, elevator, 0);
        var deflections = controls.Select(c => Aircraft.TargetDeflection(c, input)).ToArray();
        var air = new Vec3(-airspeed * Math.Cos(alpha), 0, -airspeed * Math.Sin(alpha));
        var up = new Vec3(0, 0, 1);
        aircraft.Aero.Reset();
        var load = aircraft.Aero.Evaluate(new AeroContext(air, Vec3.Zero, Isa.SeaLevelDensity, FarFromGround, up, deflections, default));
        var liftDir = new Vec3(-Math.Sin(alpha), 0, Math.Cos(alpha));
        return (Vec3.Dot(load.Force, liftDir), load.Moment.Y);
    }

    /// <summary>Neutral point aft of the CG (m) and static margin (fraction of MAC) at this alpha, elevator neutral.</summary>
    public static (double NeutralPointAft, double Margin) StaticMargin(AircraftDefinition def, double airspeed, double alpha, double elevator = 0)
    {
        var aircraft = new Aircraft(def);
        const double h = 0.5 * Math.PI / 180;
        var lo = Loads(aircraft, airspeed, alpha - h, elevator);
        var hi = Loads(aircraft, airspeed, alpha + h, elevator);
        // A lift increment dL acting x aft of the CG gives a pitch-up moment −x·dL (body x back, z up).
        double aft = -(hi.PitchMoment - lo.PitchMoment) / (hi.Lift - lo.Lift);
        return (aft, aft / MeanAerodynamicChord(def));
    }

    /// <summary>Alpha (rad) and elevator command for lift = weight and zero pitch moment at this airspeed.</summary>
    public static (double Alpha, double Elevator) Trim(AircraftDefinition def, double airspeed)
    {
        var aircraft = new Aircraft(def);
        double weight = def.Mass.Mass * Aircraft.Gravity;
        double AlphaForWeight(double elevator) =>
            Bisect(a => Loads(aircraft, airspeed, a, elevator).Lift - weight, Angle.Rad(-8), Angle.Rad(12));
        double elevator = Bisect(e => -Loads(aircraft, airspeed, AlphaForWeight(e), e).PitchMoment, -1, 1);
        return (AlphaForWeight(elevator), elevator);
    }

    /// <summary>Root of an increasing-or-decreasing function on [lo, hi] by bisection (clamps to the ends if no sign change).</summary>
    static double Bisect(Func<double, double> f, double lo, double hi)
    {
        double flo = f(lo);
        if (Math.Sign(flo) == Math.Sign(f(hi))) return Math.Abs(flo) < Math.Abs(f(hi)) ? lo : hi;
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            double fm = f(mid);
            if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; }
            else hi = mid;
        }
        return 0.5 * (lo + hi);
    }
}
