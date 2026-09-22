using Symlab.Flight.Atmosphere;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Aero;

/// <summary>
/// Strip-theory aerodynamics: every surface is cut into spanwise segments, each evaluated with its
/// own local flow (including rotation, prop wash, downwash and ground effect) and airfoil polar.
/// </summary>
public sealed class SurfaceAeroModel : IAeroModel
{
    const double DownwashGain = 1.6;
    const double FlapEfficiency = 0.85;
    const double MaxDownwash = 0.3;

    readonly SurfaceSegment[] _segments;
    readonly BodySpec[] _bodies;
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

        foreach (var seg in _segments)
        {
            var u = ctx.AirVelocityBody + Vec3.Cross(ctx.AngularVelocityBody, seg.Position) + WashAt(ctx.Wash, seg.Position);
            var c = seg.ChordAxis;
            var n = seg.NormalAxis;
            double uc = Vec3.Dot(u, c);
            double un = Vec3.Dot(u, n);
            double v = Math.Sqrt(uc * uc + un * un);
            if (v < 0.1) continue;

            double q = 0.5 * ctx.Density * v * v;
            double alpha = Math.Atan2(-un, uc);
            double height = ctx.HeightAboveGround + Vec3.Dot(seg.Position, ctx.UpBody);
            double groundEffect = InducedFlow.GroundEffectFactor(height, WingSpan);
            double delta = seg.ControlIndex >= 0 ? ctx.Deflections[seg.ControlIndex] : 0;

            double alphaGeo = alpha + seg.FlapEffectiveness * FlapEfficiency * delta;
            if (seg.Role == SurfaceRole.HorizontalTail) alphaGeo -= Downwash * groundEffect;

            double reynolds = ctx.Density * v * seg.Chord / Isa.DynamicViscosity;
            double ai = InducedFlow.SolveInducedAngle(seg.Airfoil, reynolds, alphaGeo, seg.InducedFactor * groundEffect);
            var coeff = seg.Airfoil.Evaluate(alphaGeo - ai, reynolds);
            double sinDelta = Math.Sin(delta);
            double cl = coeff.Cl * Math.Cos(ai);
            double cd = coeff.Cd + coeff.Cl * Math.Sin(ai) + seg.ControlChordFraction * sinDelta * sinDelta;

            var liftDir = (c * -un + n * uc) / v;
            var dragDir = (c * uc + n * un) / -v;
            double qa = q * seg.Area;
            var f = (liftDir * cl + dragDir * cd) * qa;
            force += f;
            double cm = coeff.Cm + seg.FlapMomentEffectiveness * FlapEfficiency * delta;
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

    static Vec3 WashAt(in PropWash wash, Vec3 p)
    {
        if (wash.Velocity <= 0 || wash.Radius <= 0) return Vec3.Zero;
        var rel = p - wash.PositionBody;
        double along = Vec3.Dot(rel, wash.AxisBody);
        if (along > 0) return Vec3.Zero;
        var radial = rel - wash.AxisBody * along;
        return radial.Length <= wash.Radius ? wash.AxisBody * wash.Velocity : Vec3.Zero;
    }

    void AssignControl(ControlSurfaceSpec control, int index)
    {
        int covered = 0;
        foreach (var seg in _segments)
        {
            if (seg.SurfaceName != control.Surface) continue;
            if (control.Side != Side.Both && seg.Side != control.Side) continue;
            if (seg.SpanFraction < control.SpanStart || seg.SpanFraction > control.SpanEnd) continue;
            if (seg.ControlIndex >= 0)
                throw new ArgumentException($"Control '{control.Name}' overlaps another control on surface '{control.Surface}'.");
            seg.ControlIndex = index;
            seg.FlapEffectiveness = SurfaceGeometry.FlapEffectiveness(control.ChordFraction);
            seg.FlapMomentEffectiveness = SurfaceGeometry.FlapMomentCoefficient(control.ChordFraction);
            seg.ControlChordFraction = control.ChordFraction;
            covered++;
        }
        if (covered == 0)
            throw new ArgumentException($"Control '{control.Name}' does not cover any segment of surface '{control.Surface}'.");
    }

    static Airfoil Lookup(IReadOnlyDictionary<string, Airfoil> airfoils, SurfaceSpec s) =>
        airfoils.TryGetValue(s.Airfoil, out var a)
            ? a
            : throw new ArgumentException($"Surface '{s.Name}' references unknown airfoil '{s.Airfoil}'.");
}
