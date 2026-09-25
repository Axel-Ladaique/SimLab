using SimLab.Flight.Geometry;

namespace SimLab.Flight.Aero;

/// <summary>
/// The slipstream at one station a distance <see cref="Distance"/> behind the prop disk: an axial velocity profile
/// u(r) = <see cref="CentreVelocity"/>·g(r) with g = 1 inside <see cref="CoreRadius"/>, a cosine taper to 0 across
/// <see cref="EdgeWidth"/>, and a swirl v_θ(r) = <see cref="SwirlRate"/>·r·g(r) turning with the prop.
/// </summary>
/// <param name="TubeRadius">Radius of the inviscid actuator-disk stream tube (R at the disk, R/√2 far behind a static prop).</param>
public readonly record struct WashStation(double Distance, double TubeRadius, double CentreVelocity, double CoreRadius, double EdgeWidth, double SwirlRate)
{
    public double OuterRadius => CoreRadius + EdgeWidth;

    public double HalfVelocityRadius => CoreRadius + 0.5 * EdgeWidth;

    /// <summary>Radial profile g(r): 1 in the core, ½(1 + cos(π(r − a)/L)) across the mixing layer, 0 outside.</summary>
    public double Profile(double r)
    {
        if (CentreVelocity <= 0 || r >= OuterRadius) return 0;
        if (r <= CoreRadius) return 1;
        return 0.5 * (1 + Math.Cos(Math.PI * (r - CoreRadius) / EdgeWidth));
    }

    /// <summary>Axial slipstream velocity (m/s, directed aft, opposite the thrust) at radius r.</summary>
    public double AxialVelocity(double r) => CentreVelocity * Profile(r);

    /// <summary>Swirl velocity (m/s, in the prop's direction of rotation) at radius r.</summary>
    public double SwirlVelocity(double r) => SwirlRate * r * Profile(r);
}

/// <summary>
/// Propeller slipstream ("prop wash") in body axes: axial velocity from actuator-disk momentum theory with the stream-tube
/// contraction, then turbulent mixing (the jet spreads and slows downstream while its momentum flux stays equal to the
/// thrust), plus a swirl carrying (a calibrated share of) the prop torque as angular momentum flux. Nothing is induced
/// ahead of the disk.
/// </summary>
/// <remarks>
/// Formulas and sources:
/// <list type="bullet">
/// <item>Induced velocity at the disk (momentum theory with axial inflow V₀): T = 2ρA v_i (V₀ + v_i), so
/// v_i = −V₀/2 + √(V₀²/4 + T/(2ρA)); the far wake reaches 2 v_i (Glauert; McCormick, <i>Aerodynamics, Aeronautics and
/// Flight Mechanics</i>, 2nd ed., §6.2).</item>
/// <item>Axial development of an actuator disk (vortex-cylinder / uniformly loaded disk solution, on the axis):
/// w(s) = v_i (1 + s/√(s² + R²)), i.e. v_i at the disk, 1.71 v_i one radius behind, → 2 v_i (McCormick,
/// <i>Aerodynamics of V/STOL Flight</i>, ch. 4; Conway, J. Fluid Mech. 297, 1995). The stream-tube radius follows from
/// continuity: R_s = R √((V₀ + v_i)/(V₀ + w(s))), → R/√2 for a static prop.</item>
/// <item>Mixing: a round jet keeps a potential core (u = w(s) on the axis) that the growing shear layer erodes; the core
/// ends about 4–6 jet diameters downstream and beyond it the centreline velocity falls as 1/s at constant momentum flux
/// (Rajaratnam, <i>Turbulent Jets</i>, 1976, ch. 3; Pope, <i>Turbulent Flows</i>, 2000, §5.1). A propeller jet, with its
/// tip vortices and swirl, mixes sooner than a nozzle jet: <see cref="CoreLengthDiameters"/> = 4 contracted
/// diameters. The mixing slows with coflow in proportion to the Brown–Roshko velocity-ratio parameter
/// (U₁ − U₂)/(U₁ + U₂) = 2v_i/(2v_i + 2V₀) (Brown &amp; Roshko, J. Fluid Mech. 64, 1974), so the core length is divided by
/// it and the wash stays coherent in cruise.</item>
/// <item>Profile: a cosine-tapered top hat. The inner edge starts at (1 − <see cref="TipLossFraction"/>)·R_s (the
/// loading falls to zero at the blade tip, Prandtl tip loss) and shrinks linearly to the axis at the end of the core; the
/// outer edge is solved at every station so the jet carries the inviscid momentum flux ρ (V₀ + v_i) π R² w(s), which
/// equals the thrust once the pressure has recovered (a few radii behind the disk).</item>
/// <item>Swirl: the prop torque leaves as angular momentum flux Q = ∫ρ (V₀ + u) r v_θ dA (Glauert's general momentum
/// theory). The swirl has the shape v_θ = Ω(s)·r·g(r): solid-body near the hub, peaking in the outer part of the jet and
/// vanishing outside it, like measured slipstream swirl angles (Veldhuis, <i>Propeller Wing Aerodynamic Interference</i>,
/// PhD thesis, TU Delft, 2005, ch. 3). Ω(s) is set so the flux is <see cref="SwirlEfficiency"/>·Q at every station, so it
/// decays as the jet spreads.</item>
/// </list>
/// </remarks>
public readonly record struct PropWash
{
    /// <summary>
    /// Share of the prop torque's angular momentum flux applied to the surfaces as swirl. This is a calibration, not a
    /// measured efficiency: strip theory on the full swirl over-extracts (every surface in the jet sees the whole swirl,
    /// with no depletion by the surfaces ahead of it and 2D lift slopes on strips a few centimetres wide), so the airframe
    /// would recover several times the torque (2.4 Q with this field at efficiency 1; 3.2 Q in the 2026-09-25 spike). It
    /// is set so that in a static hover the sport's wing root, stab and fin recover about 40% of the prop torque, the
    /// middle of the 30–60% plausible for a real single-engine airframe (a stator behind a prop recovers most of the
    /// swirl; a wing root, stab and fin are a partial, badly placed stator). The recovered share is linear in this
    /// constant: 30% → 0.124, 60% → 0.248.
    /// See docs/investigations/2026-09-25-slipstream-implementation.md.
    /// </summary>
    public const double SwirlEfficiency = 0.165;

    /// <summary>Fraction of the tube radius over which the blade loading (and so the velocity jump) falls to zero at the tip.</summary>
    public const double TipLossFraction = 0.15;

    /// <summary>Length of the jet's potential core in contracted jet diameters (static prop).</summary>
    public const double CoreLengthDiameters = 4.0;

    // Integrals of the cosine taper g(t) = ½(1 + cos πt) over t ∈ [0, 1].
    const double TaperT1 = 0.25 - 1 / (Math.PI * Math.PI);    // ∫ t·g dt
    const double TaperT2 = 3.0 / 16 - 1 / (Math.PI * Math.PI); // ∫ t·g² dt
    const double TaperG1 = 0.5;                                // ∫ g dt
    const double TaperG2 = 3.0 / 8;                            // ∫ g² dt

    public Vec3 PositionBody { get; init; }

    /// <summary>Unit thrust direction, body axes; the slipstream flows along −AxisBody.</summary>
    public Vec3 AxisBody { get; init; }

    public double Radius { get; init; }
    public double Thrust { get; init; }

    /// <summary>Aerodynamic torque on the prop (N·m), the angular momentum flux given to the air.</summary>
    public double Torque { get; init; }

    /// <summary>Freestream speed along the thrust axis (m/s, ≥ 0).</summary>
    public double AxialSpeed { get; init; }
    public double Density { get; init; }

    /// <summary>+1 = clockwise seen from behind, −1 = counter-clockwise; the swirl turns the same way.</summary>
    public int SpinDirection { get; init; }

    /// <summary>Momentum-theory induced velocity at the disk, m/s (0 when inactive).</summary>
    public double InducedVelocity { get; init; }

    /// <summary>Distance behind the disk where the potential core ends, m.</summary>
    public double CoreLength { get; init; }

    public bool IsActive => InducedVelocity > 0;

    public static PropWash Create(Vec3 positionBody, Vec3 axisBody, double radius, double thrust, double torque, double axialSpeed,
        double density, int spinDirection)
    {
        var wash = new PropWash
        {
            PositionBody = positionBody,
            AxisBody = axisBody,
            Radius = radius,
            Thrust = thrust,
            Torque = Math.Max(torque, 0),
            AxialSpeed = Math.Max(axialSpeed, 0),
            Density = density,
            SpinDirection = Math.Sign(spinDirection),
        };
        if (thrust <= 0 || radius <= 0 || density <= 0) return wash;

        double v0 = wash.AxialSpeed;
        double vi = -0.5 * v0 + Math.Sqrt(0.25 * v0 * v0 + thrust / (2 * density * Math.PI * radius * radius));
        double farRadius = radius * Math.Sqrt((v0 + vi) / (v0 + 2 * vi));
        double velocityRatio = 2 * vi / (2 * vi + 2 * v0);
        return wash with
        {
            InducedVelocity = vi,
            CoreLength = CoreLengthDiameters * 2 * farRadius / Math.Max(velocityRatio, 1e-6),
        };
    }

    /// <summary>The slipstream station <paramref name="distance"/> behind the disk (default when inactive or ahead of the disk).</summary>
    public WashStation At(double distance)
    {
        if (!IsActive || distance < 0) return default;
        double s = distance, v0 = AxialSpeed, vi = InducedVelocity, r0 = Radius;
        double ideal = vi * (1 + s / Math.Sqrt(s * s + r0 * r0));
        double tube = r0 * Math.Sqrt((v0 + vi) / (v0 + ideal));
        // Momentum flux over ρ of the inviscid stream tube, (V₀ + w) w π R_s² = (V₀ + v_i) w π R².
        double flux = (v0 + ideal) * ideal * Math.PI * tube * tube;

        double core, centre;
        if (s < CoreLength)
        {
            core = (1 - TipLossFraction) * tube * (1 - s / CoreLength);
            centre = ideal;
        }
        else
        {
            core = 0;
            centre = ideal * CoreLength / s;
        }

        // Momentum flux of the profile: ∫(V₀ + u) u dA = w²·I₂ + V₀ w·I₁ with, for core a and taper width L,
        // I_k = π a² + 2π L (a ∫g^k dt + L ∫t g^k dt). Quadratic in L.
        double w2 = centre * centre, vw = v0 * centre;
        double qa = 2 * Math.PI * (w2 * TaperT2 + vw * TaperT1);
        double qb = 2 * Math.PI * core * (w2 * TaperG2 + vw * TaperG1);
        double qc = Math.PI * core * core * (w2 + vw) - flux;
        double width = (-qb + Math.Sqrt(qb * qb - 4 * qa * qc)) / (2 * qa);

        var station = new WashStation(s, tube, centre, core, width, 0);
        return station with { SwirlRate = SwirlRate(station) };
    }

    /// <summary>Air velocity induced by the slipstream at a body-axes point (axial, aft, plus swirl), m/s.</summary>
    public Vec3 AirVelocityAt(Vec3 pointBody)
    {
        if (!IsActive) return Vec3.Zero;
        var rel = pointBody - PositionBody;
        double along = Vec3.Dot(rel, AxisBody);
        if (along >= 0) return Vec3.Zero;
        return AirVelocity(At(-along), rel - AxisBody * along);
    }

    /// <summary>Air velocity at a point of <paramref name="station"/> offset <paramref name="radial"/> (perpendicular to the axis) from the axis.</summary>
    public Vec3 AirVelocity(in WashStation station, Vec3 radial)
    {
        double g = station.Profile(radial.Length);
        if (g <= 0) return Vec3.Zero;
        return AxisBody * (-station.CentreVelocity * g) + Vec3.Cross(AxisBody * (SpinDirection * station.SwirlRate * g), radial);
    }

    /// <summary>Ω such that ∫ρ (V₀ + u) r (Ω r g) dA = SwirlEfficiency·Q at this station.</summary>
    double SwirlRate(in WashStation st)
    {
        if (Torque <= 0) return 0;
        double v0 = AxialSpeed, w = st.CentreVelocity, a = st.CoreRadius, width = st.EdgeWidth;
        // Core (g = 1): ∫₀ᵃ (V₀ + w) r³ 2π dr.
        double j = 2 * Math.PI * (v0 + w) * Math.Pow(a, 4) / 4;
        // Taper: 8-point Gauss–Legendre on t ∈ [0, 1], r = a + L t.
        for (int i = 0; i < GaussNodes.Length; i++)
        {
            double t = 0.5 * (GaussNodes[i] + 1);
            double r = a + width * t;
            double g = 0.5 * (1 + Math.Cos(Math.PI * t));
            j += 2 * Math.PI * (v0 + w * g) * g * r * r * r * width * 0.5 * GaussWeights[i];
        }
        return j > 0 ? SwirlEfficiency * Torque / (Density * j) : 0;
    }

    static readonly double[] GaussNodes =
        [-0.9602898564975363, -0.7966664774136267, -0.5255324099163290, -0.1834346424956498,
          0.1834346424956498, 0.5255324099163290, 0.7966664774136267, 0.9602898564975363];

    static readonly double[] GaussWeights =
        [0.1012285362903763, 0.2223810344533745, 0.3137066458778873, 0.3626837833783620,
         0.3626837833783620, 0.3137066458778873, 0.2223810344533745, 0.1012285362903763];
}
