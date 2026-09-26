# Lifting line — Design

Date: 2026-09-26. Branch: `feat/lifting-line` (from `main` @ 4f29c14), worktree `.worktrees/lifting-line`.

## Goal

Replace the strip model's induced flow (one uniform induced angle per surface, `InducedFlow.SolveInducedAngle` with
`1/(π e AR)`, and a scalar lagged downwash `1.6·CL/(πAR)` at the tail) with a nonlinear lifting line over all surfaces
together, so that the spanwise load distribution, the wing's downwash on the tail and the sidewash on the fin come
from the geometry.

Motivation: the AVL comparison of 2026-09-26 (`docs/investigations/2026-09-26-avl-comparison.md`). Ratios SimLab / AVL
before this change: Clp 1.3–1.8, Cnβ 1.5–1.8, Clβ 0.4–0.9, Cnr 1.3–1.8, static margin 1.4–1.8; every aircraft is
spirally unstable in SimLab (criterion Clβ·Cnr/(Clr·Cnβ) 0.27–0.95) where AVL gives 0.71–1.41. These are the likely
causes of realism backlog #1 (hands-off bank), #5 (spin recovery too easy) and part of #3 and #14.

## Success criteria

1. For the four aircraft (trainer, sport, wing, 3d), at the conditions of the AVL comparison (cruise airspeed, the
   alpha where lift = weight with zero deflections, sea level, out of ground effect, power off, bodies off,
   quasi-steady downwash), SimLab's stability-axis derivatives are within **±15 % of AVL** for CLα, Cmα, CYβ, Cnβ,
   Clβ, Clp, Cnr, Clr, Clδa, Cmδe and Cnδr.
2. The spiral criterion is on the same side of 1 as AVL's for every aircraft.
3. Small derivatives (Cnp, Cnδa): same sign as AVL and within an absolute band (Cnp ±0.015, Cnδa ±0.0003 per degree).
4. All behavior tests pass after section 5 (reverting compensating tunings, re-tuning with the user's approval).
5. Aero evaluation cost below 10 % of real time at 500 Hz with four RK4 evaluations per step, for every aircraft.

## 1. `LiftingLine` (new, `src/SimLab.Flight/Aero/LiftingLine.cs`)

Built once from the model's `SurfaceSegment`s. Pure computation, no Godot, no allocation per evaluation.

### Geometry

- Each strip i is a horseshoe vortex: a bound segment along its quarter-chord line from `Position − HalfSpan` to
  `Position + HalfSpan`, and two trailing legs from those ends to +∞ along body +x (frozen wake along the body axis, as
  AVL's default).
- Control point: the strip's `Position` (mid bound segment, Phillips & Snyder 2000). The strip's own bound segment is
  excluded from its own control point (collinear, no induced velocity); its own trailing legs are included.
- Influence matrix `W[i, j]` (Vec3): velocity at control point i induced by a unit circulation on horseshoe j
  (Biot–Savart for the three segments, semi-infinite legs in closed form).
- Vortex core: every segment uses a finite core (Scully / Vatistas n = 2 profile) of radius `CoreFraction · c̄`, where
  c̄ is the reference wing's mean chord and `CoreFraction` = 0.1 (a named constant), so the tail and fin strips that
  sit near a wing trailing leg get a bounded velocity.

### Solve

Unknowns Γᵢ. For each strip:

- local velocity uᵢ = air velocity at the three-quarter-chord point (freestream + ω × r₃/₄, as today) + prop wash
  (as today) + gᵢ · Σⱼ W[i, j] Γⱼ, where gᵢ is the McCormick ground-effect factor at strip i's height and the Γⱼ of a
  wing strip seen by a tail strip is the lagged value (§2);
- in-plane components on `FlowChordAxis` / `FlowNormalAxis` (simple sweep theory, as today) give vᵢ and αᵢ;
- residual Rᵢ = ½ vᵢ cᵢ clᵢ(αᵢ + flap term, Reᵢ) − Γᵢ, where cl comes from the strip's polar exactly as today.

Iteration: frozen-Jacobian Newton. With αᵢ perturbed by (nᵢ · W[i, j] Γⱼ)/vᵢ, the Jacobian of Γ ↦ ½ v c a α is
J = I − diag(½ cᵢ aᵢ) · N, N[i, j] = nᵢ · W[i, j], which does not depend on the airspeed. aᵢ is the strip polar's
attached lift slope (from the polar at its lowest-Reynolds table, finite difference around cl = 0). J is LU-factorized
once at construction. Each iteration solves J ΔΓ = R and updates Γ ← Γ + λ ΔΓ.

- Warm start from the previous solution (kept in the object; reset by `Reset()`).
- At most `MaxIterations` = 8; stop when max |R| < 1e-6 · max(1, max |Γ|).
- λ = 1 while the residual decreases, halved (down to 1/8) when it grows: stall (negative local slope) must not
  diverge. After `MaxIterations` the last Γ is used (no exception); a counter exposes non-converged evaluations for
  tests and diagnostics.
- Strips with vᵢ < 0.1 m/s carry Γᵢ = 0 (as today they are skipped).

### Outputs

For every strip, its induced velocity and Γ, read by `SurfaceAeroModel`; the mean downwash angle over the horizontal
tail strips (replaces `SurfaceAeroModel.Downwash` for tests and the diagnostics overlay).

## 2. `SurfaceAeroModel` changes

- The per-strip loop keeps its structure (sweep projection, flap, Reynolds, polar, lift ⟂ local velocity, drag along
  it, section Cm and the pitch-rate camber moment). The local velocity now includes the lifting-line induced velocity;
  the separate induced angle (`InducedFlow.SolveInducedAngle`, `SurfaceSegment.InducedFactor`) is removed. Lift is
  perpendicular to the local velocity including the induced part, so induced drag comes out of the geometry.
- The flap term keeps its current form (thin-airfoil effectiveness × DATCOM K' × 0.85 × coverage) as an angle added to
  αᵢ inside the solve.
- Removed: `DownwashGain`, `MaxDownwash`, the scalar `Downwash` state, the tail's `alphaGeo −= Downwash · groundEffect`.
- Downwash lag: for each tail surface (roles `HorizontalTail`, `VerticalTail`), τ = max(0, x̄_tail − x̄_wing) / max(V, 1)
  between the surfaces' mean quarter-chord x. The Γ of wing strips seen by that surface's strips is a copy advanced in
  `Advance(dt)` as a first-order lag with time constant τ (as today's downwash). All other influences are
  instantaneous. The flying wing's winglets sit at almost the wing's x, so their τ is near zero.
- `Evaluate` must not advance lagged states (the `IAeroModel` contract); it may update the warm-start Γ, which only
  changes the iteration's starting point, not the converged result.
- Ground effect: `InducedFlow.GroundEffectFactor(height, span)` stays, applied per receiving strip to the induced
  velocity. `InducedFlow.SolveInducedAngle` and its tests are deleted.
- Format: `oswald` is removed from `SurfaceSpec`, the DTO and `aircraft/README.md`. No shipped aircraft sets it. The
  loader rejects a surface that still has it: "oswald is no longer used: the lifting line computes the span
  efficiency". (Waxwing's copy of the format is updated in a Waxwing session, not here.)

Risk: Phillips' control point at the bound vortex is known to be less accurate near the root of swept wings. The
flying wing (25° sweep) is the check. If it misses criterion 1, stop after §4's first measurement and report before
switching to Weissinger control points at the three-quarter chord with the 2D self-induction removed.

## 3. Unit tests (`tests/SimLab.Flight.Tests/Aero/LiftingLineTests.cs`)

With the linear 2π test polar:

- Rectangular wing, AR 6: CLα within ±3 % of Helmbold, 2πA / (2 + √(A² + 4)), and converged in the strip count
  (12 vs 24 strips per side within 1 %).
- Induced drag: a wing with elliptic chords gives e = CL² / (π A CDi) between 0.95 and 1.02; a rectangular wing gives a
  lower e.
- Roll damping of the rectangular AR 6 wing below the old strip value (−a/6 per p b/2V) and within ±10 % of the
  vortex-lattice reference for that wing (computed once with AVL and stored in the test with its source).
- Downwash at a tail one semi-span behind the wing: positive, below 2 CL/(πA) and above half of it.
- Sidewash: a wing (5° dihedral) with a fin behind and above it, in 5° sideslip: the fin's side force with the wing present
  differs from the isolated fin's, and both are within ±15 % of AVL's for the same two configurations (values stored in
  the test with their source).
- Symmetry: β = 0 and ω = 0 give no roll, yaw or side force.
- Robustness: α = 40°, reversed flow and 0.05 m/s give finite loads, no NaN, and at most `MaxIterations` iterations.
- Lag: after an alpha step the tail downwash reaches 63 % of its change after τ = tail arm / V (±10 %).
- Existing `SurfaceAeroModelTests` adapted where they encoded the old model (lift of the finite wing now from
  Helmbold instead of the Oswald formula, downwash tests on the new output).

## 4. AVL comparison as a test

- `tests/SimLab.Flight.Tests/Behavior/StabilityDerivatives.cs`: the finite-difference harness of the investigation
  (`docs/investigations/2026-09-26-avl-comparison/Deriv.cs`) as a test utility: surfaces only, sea level, far from the
  ground, downwash lag converged, AVL stability axes and signs.
- `tests/SimLab.Flight.Tests/Behavior/Golden/avl-derivatives.json`: AVL's derivatives per aircraft with the airspeed,
  alpha, references and the Waxwing / AVL versions, produced by `compare_avl.py` (extended with a `--write-fixture`
  option). Tests do not need AVL.
- `AvlDerivativeTests`: criteria 1–3 per aircraft; a report test prints the full ratio table.
- A timing report test (no strict threshold except criterion 5, checked with a wide margin).
- When a geometry change alters an aircraft (section 5), the fixture is regenerated with the script (needs Waxwing and
  AVL locally); documented in the investigation note and the test's summary. An aircraft added later (e.g. from the
  parallel `feat/jet-f16` work) needs its own fixture entry to join the test.

## 5. Reverting compensating tunings

After §1–§4 are in and measured with the current data, these values go back to their originals (from
`docs/tuning-log.md`); the AVL fixture is regenerated because the geometry changes:

| Aircraft | Parameter | Current → original | Was changed for |
|---|---|---|---|
| trainer | wing `dihedralDeg` (+ wingtip hull y) | 5 → 3 (0.185 → 0.159) | torque bank / spiral |
| wing | wing `root` x | 0.107 → 0.133 | negative static margin in the old model |
| wing | `cg` x | 0.31 → 0.32 | static margin |
| wing | wing `twistDeg` | −6 → −2 | stall roll-off |
| wing | winglets `span`, `root` x (+ fin-top hull points) | 0.135 @ 0.48 → 0.12 @ 0.45 | yaw |
| wing | elevon `mix.aileron` | ∓0.75 → ∓1 | sideslip / roll |
| 3d | `cg` x; stab `incidenceDeg` | 0.52 → 0.50; −1.5 → 0 | re-trim before merge |

Kept: data fixes (rudder signs, hull point positions, sport aileron span from the fuselage side, strip counts), the
trainer's 12° elevator throw (a realistic trainer rate), test setups (cruise throttles, hand-launch pilot), the 3d's
mass and inertia (estimation fixes).

Every behavior test is then run. For the failing ones, the smallest data changes that pass are gathered into one
proposal, **submitted to the user before they are applied**, then logged in `docs/tuning-log.md`.

## 6. Order of work

1. `LiftingLine` + unit tests, nothing else touched.
2. Integration in `SurfaceAeroModel` (removals, lag, ground effect, `oswald`), derivative utility, AVL fixture and
   test; measure AVL criteria and behavior tests on the current data. Stop and report if the flying wing misses
   criterion 1.
3. Section 5 revert, fixture regeneration, behavior tests, re-tuning proposal → user approval → apply and log.
4. Golden file `frame-invariance.json` regenerated (intended physics change); realism backlog #1, #3, #5, #11, #14
   updated; investigation note with before/after ratios; `aircraft/README.md` without `oswald`.

## Out of scope

Ground-effect image vortices (the McCormick factor stays), wake roll-up or a wake that follows the flow, unsteady
(Theodorsen / Wagner) lift beyond the downwash lag, fuselage aerodynamics (side force, Munk moment), prop-wash changes,
Waxwing changes.
