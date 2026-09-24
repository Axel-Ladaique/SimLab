using SimLab.Flight.Geometry;

namespace SimLab.Flight.Aero;

public static class SurfaceGeometry
{
    public static IReadOnlyList<SurfaceSegment> Build(SurfaceSpec spec, Airfoil airfoil)
    {
        if (spec.Segments < 1) throw new ArgumentException($"Surface '{spec.Name}' needs at least one segment.");
        if (spec.Span <= 0 || spec.RootChord <= 0 || spec.TipChord <= 0)
            throw new ArgumentException($"Surface '{spec.Name}' has non-positive dimensions.");

        var dihedral = Quat.FromAxisAngle(Vec3.UnitX, -Angle.Rad(spec.DihedralDeg));
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

            var orientation = dihedral * Quat.FromAxisAngle(Vec3.UnitZ, Angle.Rad(spec.IncidenceDeg + spec.TwistDeg * tm));
            var chordAxis = orientation.Rotate(Vec3.UnitX);
            var normal = orientation.Rotate(Vec3.UnitY);
            var position = spec.Root + dihedral.Rotate(new Vec3(-spec.Span * tm * tanSweep, 0, spec.Span * tm));
            // The swept quarter-chord line turns with the section (dihedral, incidence and twist), so setting the
            // surface at incidence i is the same as flying it at angle of attack i.
            var spanAxis = orientation.Rotate(new Vec3(-tanSweep, 0, 1).Normalized());

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

    static Vec3 Mirror(Vec3 v) => new(v.X, v.Y, -v.Z);

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
