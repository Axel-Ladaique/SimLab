using SimLab.Flight.Geometry;

namespace SimLab.Flight.Aero;

/// <summary>
/// Builds surface strips in body axes (x back, y right, z up). A panel starts with its chord axis toward the leading
/// edge (−x, <see cref="BodyAxes.Forward"/>), its normal up (+z) and its span to the right (+y). Incidence rotates it
/// about +y by +i (leading edge up), dihedral about +x by +Γ (tip up; 90° makes a fin with its span up and its normal
/// to the left, −y). The swept quarter-chord line runs back by span·tanΛ; a mirrored panel is reflected as (x, −y, z).
/// </summary>
public static class SurfaceGeometry
{
    public static IReadOnlyList<SurfaceSegment> Build(SurfaceSpec spec, Airfoil airfoil)
    {
        if (spec.Segments < 1) throw new ArgumentException($"Surface '{spec.Name}' needs at least one segment.");
        if (spec.Span <= 0 || spec.RootChord <= 0 || spec.TipChord <= 0)
            throw new ArgumentException($"Surface '{spec.Name}' has non-positive dimensions.");

        var dihedral = Quat.FromAxisAngle(Vec3.UnitX, Angle.Rad(spec.DihedralDeg));
        double tanSweep = Math.Tan(Angle.Rad(spec.SweepDeg));
        double inducedFactor = 1.0 / (Math.PI * spec.Oswald * spec.AspectRatio);
        var segments = new List<SurfaceSegment>(spec.Mirror ? 2 * spec.Segments : spec.Segments);

        for (int k = 0; k < spec.Segments; k++)
        {
            double t0 = (double)k / spec.Segments;
            double t1 = (double)(k + 1) / spec.Segments;
            double tm = 0.5 * (t0 + t1);
            double c0 = spec.RootChord + (spec.TipChord - spec.RootChord) * t0;
            double c1 = spec.RootChord + (spec.TipChord - spec.RootChord) * t1;
            double chord = 0.5 * (c0 + c1);
            double area = spec.Span * (t1 - t0) * chord;

            var orientation = dihedral * Quat.FromAxisAngle(Vec3.UnitY, Angle.Rad(spec.IncidenceDeg + spec.TwistDeg * tm));
            var chordAxis = orientation.Rotate(BodyAxes.Forward);
            var normal = orientation.Rotate(BodyAxes.Up);
            var position = spec.Root + dihedral.Rotate(new Vec3(spec.Span * tm * tanSweep, spec.Span * tm, 0));
            // The swept quarter-chord line turns with the section (dihedral, incidence and twist), so setting the
            // surface at incidence i is the same as flying it at angle of attack i.
            var spanAxis = orientation.Rotate(new Vec3(tanSweep, 1, 0).Normalized());

            segments.Add(Make(spec, Side.Right, position, chordAxis, normal, spanAxis, chord, area, tm, airfoil, inducedFactor));
            if (spec.Mirror)
                segments.Add(Make(spec, Side.Left, Mirror(position), Mirror(chordAxis), Mirror(normal), Mirror(spanAxis), chord, area, tm, airfoil, inducedFactor));
        }
        return segments;
    }

    /// <summary>Thin-airfoil flap effectiveness τ for a plain flap of the given chord fraction.</summary>
    public static double FlapEffectiveness(double chordFraction)
    {
        double theta = Math.Acos(2 * Math.Clamp(chordFraction, 0, 1) - 1);
        return 1 - (theta - Math.Sin(theta)) / Math.PI;
    }

    /// <summary>Thin-airfoil quarter-chord pitching-moment coefficient per unit flap deflection, ΔCm_c/4 / δ.</summary>
    public static double FlapMomentCoefficient(double chordFraction)
    {
        double theta = Math.Acos(2 * Math.Clamp(chordFraction, 0, 1) - 1);
        return -0.5 * Math.Sin(theta) * (1 - Math.Cos(theta));
    }

    // USAF Stability and Control DATCOM, Figure 6.1.1.1-40 (also Roskam, Airplane Design Part VI, Figure 8.13):
    // empirical correction K' for plain trailing-edge flaps at large deflections, ΔCl = cl_δ,theory · K' · δ.
    // Values as digitized in Digital DATCOM (subroutine LATFLP, arrays X21126 / X11126 / Y11126).
    static readonly double[] KPrimeDeflectionsDeg = [0, 10, 12, 14, 16, 18, 20, 23, 27, 30, 35, 40, 50, 60];
    static readonly double[] KPrimeChordFractions = [0.10, 0.15, 0.20, 0.25, 0.30, 0.40, 0.50];
    static readonly double[][] KPrimeTable =
    [
        [1.000, 1.000, 0.994, 0.989, 0.970, 0.938, 0.900, 0.829, 0.755, 0.722, 0.672, 0.641, 0.596, 0.562],
        [1.000, 1.000, 0.994, 0.989, 0.970, 0.937, 0.890, 0.809, 0.737, 0.698, 0.650, 0.618, 0.569, 0.531],
        [1.000, 1.000, 0.994, 0.989, 0.968, 0.936, 0.870, 0.783, 0.710, 0.673, 0.630, 0.595, 0.542, 0.500],
        [1.000, 1.000, 0.994, 0.989, 0.965, 0.935, 0.850, 0.740, 0.677, 0.644, 0.600, 0.569, 0.518, 0.480],
        [1.000, 1.000, 0.994, 0.989, 0.963, 0.905, 0.800, 0.700, 0.643, 0.610, 0.570, 0.541, 0.496, 0.461],
        [1.000, 1.000, 0.993, 0.969, 0.924, 0.860, 0.750, 0.656, 0.606, 0.579, 0.540, 0.513, 0.471, 0.440],
        [1.000, 1.000, 0.981, 0.943, 0.880, 0.790, 0.695, 0.625, 0.571, 0.542, 0.512, 0.490, 0.450, 0.423],
    ];

    /// <summary>
    /// Empirical plain-flap correction K' for large deflections (flow separation on the flap): the effective
    /// deflection is K'·δ. K' is 1 up to 10° and falls to about 0.5 at 60°, sooner for larger flap chords.
    /// Bilinear in |δ| (degrees) and chord fraction, clamped to the table (0–60°, 0.10–0.50); even in δ,
    /// so K'·δ is odd and continuous.
    /// </summary>
    public static double LargeDeflectionFactor(double deflection, double chordFraction)
    {
        double deg = Math.Min(Math.Abs(Angle.Deg(deflection)), KPrimeDeflectionsDeg[^1]);
        double cf = Math.Clamp(chordFraction, KPrimeChordFractions[0], KPrimeChordFractions[^1]);
        var (i, u) = Bracket(KPrimeDeflectionsDeg, deg);
        var (j, v) = Bracket(KPrimeChordFractions, cf);
        double Row(int r) => KPrimeTable[r][i] + (KPrimeTable[r][i + 1] - KPrimeTable[r][i]) * u;
        return Row(j) + (Row(j + 1) - Row(j)) * v;
    }

    /// <summary>Index of the interval of the ascending grid that holds x, and the fraction of the way across it.</summary>
    static (int Index, double Fraction) Bracket(double[] grid, double x)
    {
        int i = 0;
        while (i < grid.Length - 2 && x > grid[i + 1]) i++;
        return (i, (x - grid[i]) / (grid[i + 1] - grid[i]));
    }

    /// <summary>Reflection across the aircraft's plane of symmetry (the x–z plane).</summary>
    static Vec3 Mirror(Vec3 v) => new(v.X, -v.Y, v.Z);

    static SurfaceSegment Make(SurfaceSpec spec, Side side, Vec3 position, Vec3 chordAxis, Vec3 normal, Vec3 spanAxis,
        double chord, double area, double spanFraction, Airfoil airfoil, double inducedFactor)
    {
        // The section plane of simple sweep theory is perpendicular to the swept span line; its normal is
        // perpendicular to the plane that holds the streamwise chord and the swept span line.
        var flowNormal = Vec3.Cross(spanAxis, chordAxis).Normalized();
        if (Vec3.Dot(flowNormal, normal) < 0) flowNormal = -flowNormal;
        var flowChord = Vec3.Cross(flowNormal, spanAxis);
        if (Vec3.Dot(flowChord, chordAxis) < 0) flowChord = -flowChord;
        return new SurfaceSegment
        {
            SurfaceName = spec.Name,
            Role = spec.Role,
            Side = side,
            Position = position,
            ChordAxis = chordAxis,
            NormalAxis = normal,
            Chord = chord,
            Area = area,
            SpanFraction = spanFraction,
            Airfoil = airfoil,
            InducedFactor = inducedFactor,
            FlowChordAxis = flowChord,
            FlowNormalAxis = flowNormal,
        };
    }
}
