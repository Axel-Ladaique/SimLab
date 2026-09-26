using SimLab.Flight.Atmosphere;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Aero;

/// <summary>
/// Strip-theory aerodynamics on a lifting line: every surface is cut into spanwise strips, each with its own local flow
/// (rotation, prop wash, ground effect) and airfoil polar; the induced flow of all strips on each other (span loading,
/// the wing's downwash on the tail, sidewash on the fin) comes from <see cref="LiftingLine"/>.
/// </summary>
public sealed class SurfaceAeroModel : IAeroModel
{
    const double FlapEfficiency = 0.85;
    // High-lift flaps (driven by the flap channel): this share of the flap's equivalent alpha shift is applied as a lift
    // increment on top of the section polar, so the maximum lift rises and the stall angle drops only by the rest, as for
    // plain flaps (DATCOM section 6.1.1.3). Other controls stay a pure alpha shift.
    const double HighLiftIncrementShare = 0.6;
    const double SectionLiftSlope = 5.7;

    /// <summary>Points per strip at which the prop wash is sampled (overlap weighting).</summary>
    const int WashSamples = 8;

    /// <summary>Length of the trailing legs over the chord, quarter chord to trailing edge, as a fraction of the chord.</summary>
    const double ChordwiseLegFraction = 0.75;

    readonly SurfaceSegment[] _segments;
    readonly bool[] _inWash;
    readonly Vec3[] _washAir;
    PropWash _cachedWash;
    bool _washValid;
    readonly BodySpec[] _bodies;
    readonly List<ControlSurfaceSpec> _assigned = [];
    readonly LiftingLine _line;
    readonly StripState[] _states;
    readonly Vec3[] _blown;
    readonly double[] _meanSquare;
    double _lastAirspeed;

    public SurfaceAeroModel(
        IEnumerable<SurfaceSpec> surfaces,
        IReadOnlyDictionary<string, Airfoil> airfoils,
        IReadOnlyList<ControlSurfaceSpec> controls,
        IEnumerable<BodySpec> bodies)
    {
        var specs = surfaces.ToArray();
        _segments = specs.SelectMany(s => SurfaceGeometry.Build(s, Lookup(airfoils, s))).ToArray();
        _inWash = new bool[_segments.Length];
        _washAir = new Vec3[_segments.Length * WashSamples];
        _bodies = bodies.ToArray();

        var wings = specs.Where(s => s.Role == SurfaceRole.Wing).ToArray();
        WingSpan = wings.Length > 0 ? wings.Max(s => s.TotalSpan) : 0;
        WingArea = wings.Sum(s => s.TotalArea);

        for (int i = 0; i < controls.Count; i++) AssignControl(controls[i], i);

        _line = new LiftingLine(_segments);
        _states = new StripState[_segments.Length];
        _blown = new Vec3[_segments.Length];
        _meanSquare = new double[_segments.Length];
    }

    public IReadOnlyList<SurfaceSegment> Segments => _segments;
    public double WingSpan { get; }
    public double WingArea { get; }

    /// <summary>The lifting line (read it; <see cref="Evaluate"/>, <see cref="Advance"/> and <see cref="Reset"/> drive it).</summary>
    public LiftingLine Line => _line;

    /// <summary>
    /// Mean reduction of the horizontal-tail strips' angle of attack by the induced flow in the last <see cref="Evaluate"/>
    /// (rad; 0 without a horizontal tail). For tests and diagnostics.
    /// </summary>
    public double TailDownwash { get; private set; }

    public BodyLoad Evaluate(in AeroContext ctx)
    {
        var force = Vec3.Zero;
        var moment = Vec3.Zero;

        bool blown = ctx.Wash.IsActive;
        if (blown) UpdateWashSamples(ctx.Wash);

        for (int i = 0; i < _segments.Length; i++)
        {
            var seg = _segments[i];
            // The rotation is taken at the three-quarter-chord point (Pistolesi): a section pitching about its quarter
            // chord lifts as if at the angle of attack seen there. It is also the lifting line's control point.
            var onset = ctx.AirVelocityBody + Vec3.Cross(ctx.AngularVelocityBody, LiftingLine.ControlPoint(seg));
            // The lifting line sees the onset flow only; the prop wash is applied strip-wise on top of its induced flow.
            // The lifting line carries the freestream-driven span loading, the slipstream stays strip theory: a blown
            // band in a jet is not an isolated low-aspect-ratio wing (static deflected-slipstream tests, NACA TR 1263,
            // turn the jet far more than such a wing would).
            // Mean in-plane speed squared over the strip; differs from v² only where the prop wash varies across it.
            double meanSquare = -1;
            _blown[i] = blown ? Blow(i, onset, seg.FlowChordAxis, seg.FlowNormalAxis, out meanSquare) : onset;
            _meanSquare[i] = meanSquare;
            double height = ctx.HeightAboveGround + Vec3.Dot(seg.Position, ctx.UpBody);
            double shift = seg.FlapEffectiveness * Flap(seg, ctx, out _);
            double increment = seg.HighLift ? HighLiftIncrementShare * shift : 0;
            _states[i] = new StripState(onset, shift - increment, InducedFlow.GroundEffectFactor(height, WingSpan),
                SectionLiftSlope * increment);
        }

        _line.Solve(_states, ctx.Density);

        double downwash = 0;
        int tailStrips = 0;
        for (int i = 0; i < _segments.Length; i++)
        {
            var seg = _segments[i];
            var c = seg.FlowChordAxis;
            var n = seg.FlowNormalAxis;
            // Onset flow with the prop wash (strip theory); the lifting line's induced flow is subtracted below.
            var u = _blown[i];
            double ubc = Vec3.Dot(u, c), ubn = Vec3.Dot(u, n);
            double vBlown = Math.Sqrt(ubc * ubc + ubn * ubn);

            // Section angle of attack at the control point, with the lifting line's induced flow.
            var uSection = u - _line.InducedAtControlPoint(i);
            double sc = Vec3.Dot(uSection, c), sn = Vec3.Dot(uSection, n);
            if (vBlown < 0.1 || sc * sc + sn * sn < 0.01) continue;
            double alphaSection = Math.Atan2(-sn, sc);
            double reynolds = ctx.Density * vBlown * seg.Chord / Isa.DynamicViscosity;
            var coeff = seg.Airfoil.Evaluate(alphaSection + _states[i].AlphaOffset, reynolds);
            if (seg.Role == SurfaceRole.HorizontalTail)
            {
                downwash += Math.Atan2(-ubn, ubc) - alphaSection;
                tailStrips++;
            }

            // Lift and drag directions from the flow at the bound vortex; the dynamic pressure from the onset speed with the
            // wash, as the circulation (the induced flow turns the force, it does not feed its magnitude).
            var uForce = u - _line.InducedAtBoundVortex(i);
            double uc = Vec3.Dot(uForce, c), un = Vec3.Dot(uForce, n);
            double v = Math.Sqrt(uc * uc + un * un);
            if (v < 0.1) continue;
            double q = 0.5 * ctx.Density * Math.Max(vBlown * vBlown, _meanSquare[i]);
            double flap = Flap(seg, ctx, out double delta);
            double sinDelta = Math.Sin(delta);
            double cl = coeff.Cl + _states[i].ClIncrement;
            double cd = coeff.Cd + seg.ControlCoverage * seg.ControlChordFraction * sinDelta * sinDelta;

            var liftDir = (c * -un + n * uc) / v;
            var dragDir = (c * uc + n * un) / -v;
            double qa = q * seg.Area;
            var f = (liftDir * cl + dragDir * cd) * qa;
            force += f;
            // The linear normal wash of a pitching section is a parabolic camber: ΔCm_c/4 = −(π/4)·q c / (2V) (thin airfoil).
            double pitchRate = Vec3.Dot(ctx.AngularVelocityBody, seg.PitchAxis);
            double cm = coeff.Cm + seg.FlapMomentEffectiveness * flap - Math.PI / 4 * pitchRate * seg.Chord / (2 * vBlown);
            moment += Vec3.Cross(seg.Position, f) + seg.PitchAxis * (qa * seg.Chord * cm);
            // The trailing legs over the chord carry the circulation in the local flow. Each end of the strip has its own
            // leg, as long as the chord there: with taper the two legs differ, so the forces are summed leg by leg (a pure
            // couple only for a constant chord), and where surfaces meet (a winglet on a wing tip) the legs sit at the
            // junction with each surface's own chord. In sideslip it is the lift-dependent part of the dihedral effect.
            // It uses the lifting line's (freestream-driven) Γ with the local blown flow.
            var legForce = Vec3.Cross(-uForce, Vec3.UnitX) * (ctx.Density * _line.Circulation(i) * ChordwiseLegFraction);
            bool outerIsEnd = Vec3.Dot(_line.BoundVector(i), seg.HalfSpan) > 0;
            var outer = seg.Position + seg.HalfSpan;
            var inner = seg.Position - seg.HalfSpan;
            // A leg's force acts at its middle, from the quarter chord halfway to the trailing edge.
            var outerForce = legForce * (outerIsEnd ? seg.OuterChord : -seg.OuterChord);
            var innerForce = legForce * (outerIsEnd ? -seg.InnerChord : seg.InnerChord);
            var outerAt = outer - seg.ChordAxis * (0.5 * ChordwiseLegFraction * seg.OuterChord);
            var innerAt = inner - seg.ChordAxis * (0.5 * ChordwiseLegFraction * seg.InnerChord);
            force += outerForce + innerForce;
            moment += Vec3.Cross(outerAt, outerForce) + Vec3.Cross(innerAt, innerForce);
        }
        TailDownwash = tailStrips > 0 ? downwash / tailStrips : 0;

        foreach (var body in _bodies)
        {
            var drag = BodyDrag(body.Position, body.CdA, ctx.AirVelocityBody, ctx.AngularVelocityBody, ctx.Density);
            force += drag.Force;
            moment += drag.Moment;
        }

        _lastAirspeed = ctx.AirVelocityBody.Length;
        return new BodyLoad(force, moment);
    }

    /// <summary>Drag of a bluff body with per-axis drag areas (m², body order [frontal, side, top]) at a body-axes point.</summary>
    public static BodyLoad BodyDrag(Vec3 position, Vec3 cdA, Vec3 airVelocityBody, Vec3 angularVelocityBody, double density)
    {
        var u = airVelocityBody + Vec3.Cross(angularVelocityBody, position);
        double k = -0.5 * density * u.Length;
        var f = new Vec3(u.X * cdA.X, u.Y * cdA.Y, u.Z * cdA.Z) * k;
        return new BodyLoad(f, Vec3.Cross(position, f));
    }

    public void Advance(double dt) => _line.Advance(dt, _lastAirspeed);

    public void Reset()
    {
        _line.Reset();
        _lastAirspeed = 0;
        TailDownwash = 0;
    }

    /// <summary>Effective flap angle of a strip (rad; 0 without a control) and its raw deflection.</summary>
    static double Flap(SurfaceSegment seg, in AeroContext ctx, out double delta)
    {
        delta = seg.ControlIndex >= 0 ? ctx.Deflections[seg.ControlIndex] : 0;
        // Plain flaps lose effectiveness at large deflections as the flow separates on the flap.
        return delta == 0 ? 0 : seg.ControlCoverage * FlapEfficiency * SurfaceGeometry.LargeDeflectionFactor(delta, seg.ControlChordFraction) * delta;
    }

    /// <summary>
    /// Adds the prop wash to a strip's relative velocity <paramref name="u"/>. The wash is sampled at
    /// <see cref="WashSamples"/> points spread evenly along the strip's quarter-chord line, so a strip partly inside the
    /// slipstream is weighted by its actual overlap. Returns the mean relative velocity (it sets the angle of attack) and,
    /// in <paramref name="meanSquare"/>, the mean of the squared in-plane speed (it sets the dynamic pressure); −1 if the
    /// strip is outside the slipstream.
    /// </summary>
    Vec3 Blow(int index, Vec3 u, Vec3 c, Vec3 n, out double meanSquare)
    {
        meanSquare = -1;
        if (!_inWash[index]) return u;
        var sum = Vec3.Zero;
        double squares = 0;
        for (int k = 0; k < WashSamples; k++)
        {
            var uk = u - _washAir[index * WashSamples + k];
            double ukc = Vec3.Dot(uk, c), ukn = Vec3.Dot(uk, n);
            sum += uk;
            squares += ukc * ukc + ukn * ukn;
        }
        meanSquare = squares / WashSamples;
        return sum / WashSamples;
    }

    /// <summary>
    /// Air velocity induced by the wash at each strip's sample points. It depends only on the wash and the geometry, so it
    /// is kept while the wash is unchanged (the four evaluations of an RK4 step share one wash). The station (distance
    /// behind the disk) is taken at the strip's centre.
    /// </summary>
    void UpdateWashSamples(in PropWash wash)
    {
        if (_washValid && wash == _cachedWash) return;
        _cachedWash = wash;
        _washValid = true;
        for (int i = 0; i < _segments.Length; i++)
        {
            var seg = _segments[i];
            _inWash[i] = false;
            var rel = seg.Position - wash.PositionBody;
            double along = Vec3.Dot(rel, wash.AxisBody);
            if (along >= 0) continue;
            var station = wash.At(-along);
            var radial = rel - wash.AxisBody * along;
            var halfSpan = seg.HalfSpan - wash.AxisBody * Vec3.Dot(seg.HalfSpan, wash.AxisBody);
            if (radial.Length - halfSpan.Length >= station.OuterRadius) continue;
            _inWash[i] = true;
            for (int k = 0; k < WashSamples; k++)
            {
                double t = (2.0 * k + 1) / WashSamples - 1;
                _washAir[i * WashSamples + k] = wash.AirVelocity(station, radial + halfSpan * t);
            }
        }
    }

    void AssignControl(ControlSurfaceSpec control, int index)
    {
        foreach (var other in _assigned)
        {
            bool sameSide = control.Side == Side.Both || other.Side == Side.Both || control.Side == other.Side;
            if (other.Surface == control.Surface && sameSide
                && Math.Min(control.SpanEnd, other.SpanEnd) > Math.Max(control.SpanStart, other.SpanStart))
                throw new ArgumentException($"Control '{control.Name}' overlaps another control on surface '{control.Surface}'.");
        }
        _assigned.Add(control);

        int covered = 0;
        foreach (var seg in _segments)
        {
            if (seg.SurfaceName != control.Surface) continue;
            if (control.Side != Side.Both && seg.Side != control.Side) continue;
            double lo = seg.SpanFraction - seg.SpanFractionHalfWidth, hi = seg.SpanFraction + seg.SpanFractionHalfWidth;
            double coverage = (Math.Min(hi, control.SpanEnd) - Math.Max(lo, control.SpanStart)) / (hi - lo);
            if (coverage <= 1e-9) continue;
            covered++;
            // A strip shared by two adjacent controls keeps the one covering more of it (at most half a strip is lost).
            if (seg.ControlIndex >= 0 && seg.ControlCoverage >= coverage) continue;
            seg.ControlIndex = index;
            seg.ControlCoverage = Math.Min(1, coverage);
            seg.FlapEffectiveness = SurfaceGeometry.FlapEffectiveness(control.ChordFraction);
            seg.FlapMomentEffectiveness = SurfaceGeometry.FlapMomentCoefficient(control.ChordFraction);
            seg.ControlChordFraction = control.ChordFraction;
            seg.HighLift = control.Mix.ContainsKey("flap");
        }
        if (covered == 0)
            throw new ArgumentException($"Control '{control.Name}' does not cover any segment of surface '{control.Surface}'.");
    }

    static Airfoil Lookup(IReadOnlyDictionary<string, Airfoil> airfoils, SurfaceSpec s) =>
        airfoils.TryGetValue(s.Airfoil, out var a)
            ? a
            : throw new ArgumentException($"Surface '{s.Name}' references unknown airfoil '{s.Airfoil}'.");
}
