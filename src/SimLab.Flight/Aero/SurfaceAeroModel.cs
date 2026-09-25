using SimLab.Flight.Atmosphere;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Aero;

/// <summary>
/// Strip-theory aerodynamics: every surface is cut into spanwise segments, each evaluated with its
/// own local flow (including rotation, prop wash, downwash and ground effect) and airfoil polar.
/// </summary>
public sealed class SurfaceAeroModel : IAeroModel
{
    const double DownwashGain = 1.6;
    const double FlapEfficiency = 0.85;
    const double MaxDownwash = 0.3;

    /// <summary>Points per strip at which the prop wash is sampled (overlap weighting).</summary>
    const int WashSamples = 8;

    readonly SurfaceSegment[] _segments;
    readonly bool[] _inWash;
    readonly Vec3[] _washAir;
    PropWash _cachedWash;
    bool _washValid;
    readonly BodySpec[] _bodies;
    readonly List<ControlSurfaceSpec> _assigned = [];
    readonly double _wingAspectRatio;
    readonly double _tailArm;
    double _lastWingCl;
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
        _wingAspectRatio = WingArea > 0 ? WingSpan * WingSpan / WingArea : 0;

        var wingSegments = _segments.Where(s => s.Role == SurfaceRole.Wing).ToArray();
        var tailSegments = _segments.Where(s => s.Role == SurfaceRole.HorizontalTail).ToArray();
        _tailArm = wingSegments.Length > 0 && tailSegments.Length > 0
            ? Math.Max(0.1, Math.Abs(wingSegments.Average(s => s.Position.X) - tailSegments.Average(s => s.Position.X)))
            : 0.1;

        for (int i = 0; i < controls.Count; i++) AssignControl(controls[i], i);
    }

    public IReadOnlyList<SurfaceSegment> Segments => _segments;
    public double WingSpan { get; }
    public double WingArea { get; }
    public double Downwash { get; private set; }

    public BodyLoad Evaluate(in AeroContext ctx)
    {
        var force = Vec3.Zero;
        var moment = Vec3.Zero;
        double wingLift = 0, wingQa = 0;

        bool blown = ctx.Wash.IsActive;
        if (blown) UpdateWashSamples(ctx.Wash);

        for (int i = 0; i < _segments.Length; i++)
        {
            var seg = _segments[i];
            var u = ctx.AirVelocityBody + Vec3.Cross(ctx.AngularVelocityBody, seg.Position);
            var c = seg.FlowChordAxis;
            var n = seg.FlowNormalAxis;
            // Mean in-plane speed squared over the strip; differs from v² only where the prop wash varies across it.
            double meanSquare = -1;
            if (blown) u = Blow(i, u, c, n, out meanSquare);
            double uc = Vec3.Dot(u, c);
            double un = Vec3.Dot(u, n);
            double v = Math.Sqrt(uc * uc + un * un);
            if (v < 0.1) continue;

            double q = 0.5 * ctx.Density * Math.Max(v * v, meanSquare);
            double alpha = Math.Atan2(-un, uc);
            double height = ctx.HeightAboveGround + Vec3.Dot(seg.Position, ctx.UpBody);
            double groundEffect = InducedFlow.GroundEffectFactor(height, WingSpan);
            double delta = seg.ControlIndex >= 0 ? ctx.Deflections[seg.ControlIndex] : 0;
            // Plain flaps lose effectiveness at large deflections as the flow separates on the flap.
            double flap = delta == 0 ? 0 : seg.ControlCoverage * FlapEfficiency * SurfaceGeometry.LargeDeflectionFactor(delta, seg.ControlChordFraction) * delta;

            double alphaGeo = alpha + seg.FlapEffectiveness * flap;
            if (seg.Role == SurfaceRole.HorizontalTail) alphaGeo -= Downwash * groundEffect;

            double reynolds = ctx.Density * v * seg.Chord / Isa.DynamicViscosity;
            double ai = InducedFlow.SolveInducedAngle(seg.Airfoil, reynolds, alphaGeo, seg.InducedFactor * groundEffect);
            var coeff = seg.Airfoil.Evaluate(alphaGeo - ai, reynolds);
            double sinDelta = Math.Sin(delta);
            double cl = coeff.Cl * Math.Cos(ai);
            double cd = coeff.Cd + coeff.Cl * Math.Sin(ai) + seg.ControlCoverage * seg.ControlChordFraction * sinDelta * sinDelta;

            var liftDir = (c * -un + n * uc) / v;
            var dragDir = (c * uc + n * un) / -v;
            double qa = q * seg.Area;
            var f = (liftDir * cl + dragDir * cd) * qa;
            force += f;
            double cm = coeff.Cm + seg.FlapMomentEffectiveness * flap;
            moment += Vec3.Cross(seg.Position, f) + seg.PitchAxis * (qa * seg.Chord * cm);

            if (seg.Role == SurfaceRole.Wing)
            {
                wingLift += qa * cl;
                wingQa += qa;
            }
        }

        foreach (var body in _bodies)
        {
            var u = ctx.AirVelocityBody + Vec3.Cross(ctx.AngularVelocityBody, body.Position);
            double k = -0.5 * ctx.Density * u.Length;
            var f = new Vec3(u.X * body.CdA.X, u.Y * body.CdA.Y, u.Z * body.CdA.Z) * k;
            force += f;
            moment += Vec3.Cross(body.Position, f);
        }

        _lastWingCl = wingQa > 0 ? wingLift / wingQa : 0;
        _lastAirspeed = ctx.AirVelocityBody.Length;
        return new BodyLoad(force, moment);
    }

    public void Advance(double dt)
    {
        if (_wingAspectRatio <= 0) return;
        double target = Math.Clamp(DownwashGain * _lastWingCl / (Math.PI * _wingAspectRatio), -MaxDownwash, MaxDownwash);
        double tau = _tailArm / Math.Max(_lastAirspeed, 1.0);
        Downwash += (target - Downwash) * Math.Min(1.0, dt / tau);
    }

    public void Reset()
    {
        Downwash = 0;
        _lastWingCl = 0;
        _lastAirspeed = 0;
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
        }
        if (covered == 0)
            throw new ArgumentException($"Control '{control.Name}' does not cover any segment of surface '{control.Surface}'.");
    }

    static Airfoil Lookup(IReadOnlyDictionary<string, Airfoil> airfoils, SurfaceSpec s) =>
        airfoils.TryGetValue(s.Airfoil, out var a)
            ? a
            : throw new ArgumentException($"Surface '{s.Name}' references unknown airfoil '{s.Airfoil}'.");
}
