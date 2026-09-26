using SimLab.Flight.Atmosphere;
using SimLab.Flight.Geometry;
using SimLab.Flight.Numerics;

namespace SimLab.Flight.Aero;

/// <param name="Velocity">
/// Onset velocity of the strip's three-quarter-chord point relative to the air, body axes (m/s): freestream and rotation,
/// without the lifting line's own induced flow (the <see cref="AeroContext"/> convention). The prop wash is not part of it:
/// <see cref="SurfaceAeroModel"/> applies the slipstream strip-wise, outside the lifting line.
/// </param>
/// <param name="AlphaOffset">Angle added to the section angle of attack (rad): the flap term.</param>
/// <param name="GroundFactor">Factor on the induced flow the strip receives (McCormick ground effect, 1 out of ground effect).</param>
/// <param name="ClIncrement">Section lift coefficient added on top of the polar: the high-lift flap increment.</param>
public readonly record struct StripState(Vec3 Velocity, double AlphaOffset, double GroundFactor, double ClIncrement = 0);

/// <summary>
/// Weissinger-type lifting line over all strips of all surfaces, with each strip's own section polar
/// (docs/superpowers/specs/2026-09-26-lifting-line-design.md, section 1).
/// </summary>
/// <remarks>
/// Each strip is a horseshoe vortex: a bound segment on its quarter-chord line and two trailing legs to +∞ along body +x.
/// The section angle of attack is taken at the three-quarter-chord control point, where every horseshoe's flow is summed
/// and the two-dimensional self-induction of the strip's own bound vortex, a normal flow Γ/(π cₙ), is removed because the
/// polar already contains it. Γᵢ = ½ vᵢ cₙᵢ clᵢ with cₙ = c·cosΛ, the chord normal to the bound vortex, and vᵢ the strip's
/// onset in-plane speed, as in Prandtl's lifting line: the induced flow sets the angle of attack only, so Γ stays bounded
/// by ½ v cₙ max|cl| even where the induced flow exceeds the onset flow (with v taken from the flow including the induced
/// velocity, v ∝ |w| ∝ Γ fed back on itself and diverged in a static prop hang). The Reynolds number also uses the onset
/// speed. The equations are
/// solved by Newton's method with the Jacobian I − diag(½ cₙ a)·N, kept factored between solves and refactored with each
/// strip's local lift slope a when a solve is not converged after <see cref="RefreshAfter"/> steps: polars with a
/// laminar-bubble kink near zero lift (low Reynolds) have slopes that differ from strip to strip. Tail strips see the wing
/// strips' circulation through a first-order lag of time constant (tail x − wing x) / V: the downwash takes that long to
/// travel to the tail.
/// </remarks>
public sealed class LiftingLine
{
    /// <summary>
    /// Scully core radius as a fraction of the source strip's bound-segment length or normal chord, whichever is
    /// smaller (a core comparable to the c/2 control-point offset would damp the bound vortices' 2D induction that
    /// the Γ/(π cₙ) removal assumes uncored).
    /// </summary>
    public const double CoreFraction = 0.05;

    public const int MaxIterations = 8;

    /// <summary>
    /// Convergence when max |residual| ≤ Tolerance · max(1, max |Γ|) (relative to the largest circulation). For these
    /// aircraft Γ stays below 1 m²/s, so in practice it is an absolute 1e-6 m²/s; the relative form covers larger ones.
    /// </summary>
    public const double Tolerance = 1e-6;

    /// <summary>A solve not converged after this many steps refactors the Jacobian with the local lift slopes.</summary>
    public const int RefreshAfter = 3;

    const double MinSpeed = 0.1;
    static readonly double SlopeProbe = Angle.Rad(2);

    /// <summary>Half-width of the central difference that measures a strip's local lift slope: narrow, so near a kink it
    /// gives the slope of the table segment the strip is on rather than an average across the kink.</summary>
    static readonly double LocalSlopeStep = Angle.Rad(0.1);

    /// <summary>Floor on the Jacobian's lift slope (1/rad): past the stall the slope turns negative, and damping takes over.</summary>
    const double MinSlope = 1.0;

    readonly SurfaceSegment[] _segments;
    readonly int _n;
    readonly Vec3[] _start;
    readonly Vec3[] _end;
    readonly double[] _normalChord;
    readonly double[] _slope;
    readonly double[] _initialSlope;
    readonly Vec3[] _wControl;
    readonly Vec3[] _wBound;
    readonly bool[] _isWing;
    readonly int[] _group;
    readonly double[] _groupLead;
    readonly double[][] _lagged;
    readonly double[,] _coupling;
    readonly double[,] _jacobianMatrix;
    readonly LuDecomposition _jacobian;
    readonly double[] _alpha;
    readonly double[] _reynolds;
    readonly double[] _gamma;
    readonly double[] _residual;
    readonly double[] _step;
    readonly double[] _groundFactor;
    readonly bool[] _still;
    readonly Vec3[] _inducedControl;
    readonly Vec3[] _inducedBound;

    public LiftingLine(IReadOnlyList<SurfaceSegment> segments)
    {
        _segments = segments.ToArray();
        _n = _segments.Length;
        _start = new Vec3[_n];
        _end = new Vec3[_n];
        _normalChord = new double[_n];
        _slope = new double[_n];
        for (int i = 0; i < _n; i++)
        {
            var s = _segments[i];
            var half = s.HalfSpan;
            if (half.Length < 1e-9) throw new ArgumentException($"Strip {i} of '{s.SurfaceName}' has no span.");
            // Circulation runs start → end, oriented so that the flow along +x over it lifts along the strip normal.
            if (Vec3.Dot(Vec3.Cross(Vec3.UnitX, half), s.NormalAxis) < 0) half = -half;
            _start[i] = s.Position - half;
            _end[i] = s.Position + half;
            _normalChord[i] = s.Area / (2 * half.Length);
            // Attached lift slope at the lowest-Reynolds table (Reynolds 0 clamps to it).
            double slope = (s.Airfoil.Evaluate(SlopeProbe, 0).Cl - s.Airfoil.Evaluate(-SlopeProbe, 0).Cl) / (2 * SlopeProbe);
            _slope[i] = Math.Max(slope, MinSlope);
        }

        _wControl = new Vec3[_n * _n];
        _wBound = new Vec3[_n * _n];
        for (int i = 0; i < _n; i++)
        {
            var control = ControlPoint(_segments[i]);
            var bound = _segments[i].Position;
            for (int j = 0; j < _n; j++)
            {
                double core = CoreFraction * Math.Min((_end[j] - _start[j]).Length, _normalChord[j]);
                _wControl[i * _n + j] = Horseshoe(control, j, core, includeBound: true);
                _wBound[i * _n + j] = Horseshoe(bound, j, core, includeBound: i != j);
            }
        }

        _isWing = _segments.Select(s => s.Role == SurfaceRole.Wing).ToArray();
        _group = new int[_n];
        Array.Fill(_group, -1);
        var leads = new List<double>();
        if (_isWing.Any(w => w))
        {
            double wingX = Enumerable.Range(0, _n).Where(i => _isWing[i]).Average(i => _segments[i].Position.X);
            var groups = new Dictionary<string, int>();
            for (int i = 0; i < _n; i++)
            {
                var s = _segments[i];
                if (s.Role is not (SurfaceRole.HorizontalTail or SurfaceRole.VerticalTail)) continue;
                if (!groups.TryGetValue(s.SurfaceName, out int g))
                {
                    g = groups.Count;
                    groups[s.SurfaceName] = g;
                    double surfaceX = _segments.Where(o => o.SurfaceName == s.SurfaceName).Average(o => o.Position.X);
                    leads.Add(Math.Max(0, surfaceX - wingX));
                }
                _group[i] = g;
            }
        }
        _groupLead = leads.ToArray();
        _lagged = _groupLead.Select(_ => new double[_n]).ToArray();

        _initialSlope = (double[])_slope.Clone();
        _coupling = new double[_n, _n];
        for (int i = 0; i < _n; i++)
        {
            var normal = _segments[i].FlowNormalAxis;
            for (int j = 0; j < _n; j++)
            {
                bool lagged = _group[i] >= 0 && _isWing[j];
                _coupling[i, j] = lagged ? 0 : Vec3.Dot(normal, _wControl[i * _n + j]) + (i == j ? 1 / (Math.PI * _normalChord[i]) : 0);
            }
        }
        _jacobianMatrix = new double[_n, _n];
        BuildJacobian();
        _jacobian = new LuDecomposition(_jacobianMatrix);
        _alpha = new double[_n];
        _reynolds = new double[_n];

        _gamma = new double[_n];
        _residual = new double[_n];
        _step = new double[_n];
        _groundFactor = new double[_n];
        _still = new bool[_n];
        _inducedControl = new Vec3[_n];
        _inducedBound = new Vec3[_n];
    }

    public int Count => _n;

    /// <summary>Times the Jacobian was refactored with local lift slopes (not cleared by <see cref="Reset"/>).</summary>
    public long JacobianRefreshes { get; private set; }

    /// <summary>Iterations of the last <see cref="Solve"/> (0 when the warm start already met the tolerance).</summary>
    public int LastIterations { get; private set; }

    /// <summary>Solves that stopped at <see cref="MaxIterations"/> without meeting the tolerance (not cleared by <see cref="Reset"/>).</summary>
    public long NonConvergedSolves { get; private set; }

    /// <summary>Circulation of strip i (m²/s), positive when the strip lifts along its normal.</summary>
    public double Circulation(int i) => _gamma[i];

    /// <summary>Flow induced at strip i's control point without the strip's 2D self-induction, ground factor applied.</summary>
    public Vec3 InducedAtControlPoint(int i) => _inducedControl[i];

    /// <summary>Flow induced at the middle of strip i's bound vortex (its own bound segment excluded), ground factor applied.</summary>
    public Vec3 InducedAtBoundVortex(int i) => _inducedBound[i];

    /// <summary>Strip i's bound vortex from start to end (m, body axes), in the direction of positive circulation.</summary>
    public Vec3 BoundVector(int i) => _end[i] - _start[i];

    /// <summary>Chord normal to strip i's bound vortex, c·cosΛ (m).</summary>
    public double NormalChord(int i) => _normalChord[i];

    /// <summary>The three-quarter-chord point of a strip, where its angle of attack and rotation velocity are taken.</summary>
    public static Vec3 ControlPoint(SurfaceSegment s) => s.Position - s.ChordAxis * (0.5 * s.Chord);

    public void Solve(ReadOnlySpan<StripState> strips, double density)
    {
        if (strips.Length != _n) throw new ArgumentException($"Expected {_n} strip states, got {strips.Length}.", nameof(strips));
        for (int i = 0; i < _n; i++)
        {
            var s = _segments[i];
            _groundFactor[i] = strips[i].GroundFactor;
            double uc = Vec3.Dot(strips[i].Velocity, s.FlowChordAxis), un = Vec3.Dot(strips[i].Velocity, s.FlowNormalAxis);
            _still[i] = uc * uc + un * un < MinSpeed * MinSpeed;
            if (_still[i]) _gamma[i] = 0;
        }

        double lambda = 1, previous = double.PositiveInfinity;
        bool converged = false;
        int iteration = 0;
        for (; ; iteration++)
        {
            double worst = Residuals(strips, density);
            if (worst <= Tolerance * Math.Max(1, MaxAbsGamma()))
            {
                converged = true;
                break;
            }
            if (iteration == MaxIterations) break;
            // Not converged after RefreshAfter steps: the slopes are stale, so refresh them from the current angles of attack.
            if (iteration == RefreshAfter)
            {
                RefreshJacobian();
                previous = double.PositiveInfinity;
            }
            // Damping for stall, where the Jacobian's (floored) lift slope has the wrong sign.
            lambda = worst > previous ? Math.Max(lambda / 2, 0.125) : 1;
            previous = worst;
            _jacobian.Solve(_residual, _step);
            for (int i = 0; i < _n; i++) _gamma[i] = _still[i] ? 0 : _gamma[i] + lambda * _step[i];
        }
        LastIterations = iteration;
        if (!converged) NonConvergedSolves++;
        for (int i = 0; i < _n; i++) _inducedBound[i] = Induced(_wBound, i) * _groundFactor[i];
    }

    /// <summary>Advances the lagged wing circulation seen by each tail surface (not called by <see cref="Solve"/>).</summary>
    public void Advance(double dt, double airspeed)
    {
        double speed = Math.Max(airspeed, 1.0);
        for (int g = 0; g < _groupLead.Length; g++)
        {
            double tau = _groupLead[g] / speed;
            double k = tau > 1e-9 ? Math.Min(1, dt / tau) : 1;
            var lagged = _lagged[g];
            for (int j = 0; j < _n; j++)
                if (_isWing[j]) lagged[j] += (_gamma[j] - lagged[j]) * k;
        }
    }

    public void Reset()
    {
        // Back to the construction slopes, so a reset run replays bit for bit whatever the Jacobian saw before.
        if (!_slope.AsSpan().SequenceEqual(_initialSlope))
        {
            Array.Copy(_initialSlope, _slope, _n);
            BuildJacobian();
            _jacobian.Factor(_jacobianMatrix);
        }
        Array.Clear(_gamma);
        foreach (var lagged in _lagged) Array.Clear(lagged);
        Array.Clear(_inducedControl);
        Array.Clear(_inducedBound);
        LastIterations = 0;
    }

    void BuildJacobian()
    {
        for (int i = 0; i < _n; i++)
        {
            double k = 0.5 * _normalChord[i] * _slope[i];
            for (int j = 0; j < _n; j++) _jacobianMatrix[i, j] = (i == j ? 1 : 0) - k * _coupling[i, j];
        }
    }

    /// <summary>Refactors the Jacobian with each strip's lift slope at its last angle of attack and Reynolds number.</summary>
    void RefreshJacobian()
    {
        for (int i = 0; i < _n; i++)
        {
            if (_still[i]) continue;
            var airfoil = _segments[i].Airfoil;
            double up = airfoil.Evaluate(_alpha[i] + LocalSlopeStep, _reynolds[i]).Cl;
            double down = airfoil.Evaluate(_alpha[i] - LocalSlopeStep, _reynolds[i]).Cl;
            // Floored at half the attached slope: a strip passing through the stall mid-iteration (a tail meeting the wing's
            // wash all at once) would otherwise get a near-zero slope and make Newton oscillate; damping handles the stall.
            _slope[i] = Math.Max((up - down) / (2 * LocalSlopeStep), 0.5 * _initialSlope[i]);
        }
        BuildJacobian();
        _jacobian.Factor(_jacobianMatrix);
        JacobianRefreshes++;
    }

    Vec3 Horseshoe(Vec3 p, int j, double core, bool includeBound)
    {
        var v = VortexMath.SemiInfinite(p, _end[j], Vec3.UnitX, core) - VortexMath.SemiInfinite(p, _start[j], Vec3.UnitX, core);
        return includeBound ? v + VortexMath.Segment(p, _start[j], _end[j], core) : v;
    }

    double Residuals(ReadOnlySpan<StripState> strips, double density)
    {
        double worst = 0;
        for (int i = 0; i < _n; i++)
        {
            if (_still[i])
            {
                _residual[i] = 0;
                _inducedControl[i] = Vec3.Zero;
                continue;
            }
            var s = _segments[i];
            var w = (Induced(_wControl, i) + s.FlowNormalAxis * (_gamma[i] / (Math.PI * _normalChord[i]))) * _groundFactor[i];
            _inducedControl[i] = w;
            var onset = strips[i].Velocity;
            double oc = Vec3.Dot(onset, s.FlowChordAxis), on = Vec3.Dot(onset, s.FlowNormalAxis);
            double v = Math.Sqrt(oc * oc + on * on);
            var u = onset - w;
            double alpha = Math.Atan2(-Vec3.Dot(u, s.FlowNormalAxis), Vec3.Dot(u, s.FlowChordAxis)) + strips[i].AlphaOffset;
            double reynolds = density * v * s.Chord / Isa.DynamicViscosity;
            _alpha[i] = alpha;
            _reynolds[i] = reynolds;
            double cl = s.Airfoil.Evaluate(alpha, reynolds).Cl + strips[i].ClIncrement;
            _residual[i] = 0.5 * v * _normalChord[i] * cl - _gamma[i];
            worst = Math.Max(worst, Math.Abs(_residual[i]));
        }
        return worst;
    }

    Vec3 Induced(Vec3[] influence, int i)
    {
        var sum = Vec3.Zero;
        int row = i * _n, g = _group[i];
        for (int j = 0; j < _n; j++)
        {
            double gamma = g >= 0 && _isWing[j] ? _lagged[g][j] : _gamma[j];
            if (gamma != 0) sum += influence[row + j] * gamma;
        }
        return sum;
    }

    double MaxAbsGamma()
    {
        double m = 0;
        for (int i = 0; i < _n; i++) m = Math.Max(m, Math.Abs(_gamma[i]));
        return m;
    }
}
