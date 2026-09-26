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

1. For the four aircraft (trainer, sport, wing, 3d), at the AVL fixture's conditions (per aircraft: airspeed and alpha
   stored in the fixture, sea level, out of ground effect, power off, bodies off, downwash lag settled, zero
   deflections), SimLab's stability-axis derivatives are within **±15 % of AVL** for CLα, CYβ, Cnβ, Clβ, Clp, Cnr, Clr,
   Clδa, Cmδe and Cnδr (Cnδr only where the aircraft has a rudder).
2. **Static margin** −Cmα/CLα (in fractions of the reference chord) within **±0.02 of AVL's** (±2 % MAC). This replaces
   a ±15 % band on Cmα, which is meaningless near a neutral point: on the flying wing a 3 mm shift of the aerodynamic
   centre is 26 % of Cmα (user decision, 2026-09-26).
3. The spiral criterion Clβ·Cnr/(Clr·Cnβ) is on the same side of 1 as AVL's for every aircraft.
4. Small derivatives: Cnp same sign as AVL and within ±0.015; Cnδa within ±0.0003 per degree of AVL.
5. All behavior tests pass after section 5 (reverting compensating tunings, re-tuning with the user's approval).
6. Aero evaluation cost below 10 % of real time at 500 Hz with four RK4 evaluations per step (mean `Evaluate` time
   below 50 µs) for every aircraft.

## 1. Method (validated with a throwaway prototype against AVL, 2026-09-26)

A Weissinger-type lifting line with a nonlinear section polar:

- Each strip is a horseshoe vortex: a bound segment along its quarter-chord line (from `Position − HalfSpan` to
  `Position + HalfSpan`, oriented so that the flow along +x over it lifts along the strip normal) and two trailing legs
  from its ends to +∞ along body +x (frozen wake, as AVL).
- **Control point at the three-quarter chord** (`Position − ChordAxis · c/2`, the point where the rotation velocity is
  already taken). The influence of every horseshoe, the strip's own bound segment included, is summed there; the
  two-dimensional self-induction of an infinite bound vortex at c/2, a normal velocity Γ/(π cₙ), is then removed,
  because the section polar already contains it. With a 2π polar this is exactly Weissinger's flow tangency; with a
  real polar the section keeps its own lift curve, stall included.
- **cₙ = strip area / bound-segment length = c·cosΛ**, the chord normal to the bound vortex, is the chord used in
  Γ = ½ v cₙ cl, so that the Kutta–Joukowski force on the bound segment equals the strip force of simple sweep theory.
- **Forces use the induced velocity at the bound vortex** (own bound segment excluded), not at the control point: at
  the control point the induced drag comes out 25 % too high (span efficiency 0.78 instead of AVL's 0.98 on a
  rectangular wing).
- **Chordwise trailing-leg couple**: over the chord (quarter chord to trailing edge, length 0.75 c) the trailing legs
  carry Γ in the local flow. Their two forces are equal and opposite, a couple ρ Γ (0.75 c) (B − A) × (V_flow × x̂).
  In sideslip it is the lift-dependent part of Clβ (an unswept rectangular wing at CL 0.3: Clβ −0.037 with it, AVL
  −0.037; −0.000 without it).
- Vortex core: Scully core of radius `CoreFraction` × the source strip's bound-segment length, `CoreFraction` = 0.05
  (no measurable effect on the prototype's derivatives; it keeps the fin and tail finite near wing trailing legs).

Prototype results (linear 2π polar; AVL flat plate, 12 chordwise panels):

| Case | Quantity | Prototype | AVL |
|---|---|---|---|
| Rectangular AR 6, 12 strips / side | CLα, Clp, Clβ at CL 0.3 | 4.286, −0.464, −0.037 | 4.190, −0.438, −0.037 |
| Rectangular AR 6, 24 strips / side | CLα, Clp | 4.227, −0.448 | |
| Same wing, 5° dihedral | Clβ at α 4° | −0.104 | −0.101 |
| + fin (16 strips) behind and above | CYβ, Cnβ, Clβ | −0.162, 0.079, −0.112 | −0.158, 0.076, −0.107 |
| Fin alone, 4 / 8 / 16 strips | CYβ | −0.170 / −0.155 / −0.148 | −0.143 |
| Flying-wing planform (25° sweep, taper 0.5, 8 strips) | CLα, Clp, Clβ | 3.99, −0.393, −0.073 | 3.85, −0.361, −0.059 |

Phillips' control point at the bound vortex was rejected: CLα +10 % and Clp +27 % on the rectangular wing.
Uniform strips converge slowly on low-aspect-ratio surfaces: a fin needs about 16 strips to be within 5 % of AVL, so
the tail surfaces' `segments` are part of the section 5 proposal. The swept planform's Clβ (+25 %) is the known risk;
it is measured on the complete flying wing at the stop gate of §6 step 2.

## 2. `LiftingLine` (new, `src/SimLab.Flight/Aero/LiftingLine.cs`)

Built once from the model's `SurfaceSegment`s: bound-vortex ends, cₙ, each strip polar's attached lift slope aᵢ (cl at
±2° of its lowest-Reynolds table), influence matrices at the control points and at the bound vortices, lag groups, and
the LU factorization of the Jacobian. No allocation per evaluation.

Solve, per `Evaluate` (inputs per strip: the three-quarter-chord velocity relative to the air without induced flow,
the flap angle offset and the ground-effect factor gᵢ):

- induced flow at the control point wᵢ = gᵢ (Σⱼ W[i, j] Γⱼ* + nᵢ Γᵢ/(π cₙᵢ)), where Γⱼ* is the lagged value when j is a
  wing strip and i a tail strip (below), else Γⱼ;
- residual Rᵢ = ½ vᵢ cₙᵢ clᵢ(αᵢ + flap offset, Reᵢ) − Γᵢ with vᵢ, αᵢ from uᵢ − wᵢ projected on the strip's
  `FlowChordAxis` / `FlowNormalAxis`;
- frozen-Jacobian Newton: J = I − diag(½ cₙᵢ aᵢ) · N, N[i, j] = nᵢ · W[i, j] + δᵢⱼ/(π cₙᵢ) (lagged pairs excluded), LU
  once; Γ ← Γ + λ J⁻¹R, λ = 1, halved (not below 1/8) when the residual grows; at most 8 iterations; stop when
  max |R| ≤ 1e-9 · max(1, max |Γ|); warm start from the last solution; non-converged solves are counted, never thrown;
- strips whose in-plane speed is below 0.1 m/s carry Γ = 0.

Downwash lag: every horizontal- or vertical-tail surface is a lag group with τ = max(0, x̄_surface − x̄_wing) / max(V, 1)
(mean quarter-chord x). Its strips see the wing strips' Γ through a copy advanced by `Advance(dt, airspeed)` as a
first-order lag; all other influences are instantaneous. `Evaluate` does not advance it (the `IAeroModel` contract);
the warm start does not change the converged result. `Reset()` zeroes Γ and the lagged copies (the tail starts without
downwash, as today).

## 3. `SurfaceAeroModel` changes

- The strip loop keeps sweep projection, flap terms, Reynolds, polar, section Cm and the pitch-rate camber moment.
  The section angle of attack now comes from the three-quarter-chord velocity minus the lifting line's control-point
  induced flow; lift and drag directions and the dynamic pressure from the velocity minus the bound-vortex induced
  flow; plus the chordwise trailing-leg couple.
- Removed: `InducedFlow.SolveInducedAngle` and its test, `SurfaceSegment.InducedFactor`, `SurfaceSpec.Oswald`,
  `DownwashGain`, `MaxDownwash`, the scalar `Downwash`, the tail's `− Downwash · groundEffect`, `_tailArm`.
- Added: `TailDownwash` (mean change of the horizontal-tail strips' angle of attack due to the induced flow, from the
  last `Evaluate`; for tests and diagnostics) and `Line` (the `LiftingLine`, read-only use).
- Ground effect: `InducedFlow.GroundEffectFactor(height, span)` per strip, applied to the induced flow it receives.
- Format: `oswald` is removed from `SurfaceSpec` and the DTO; the loader rejects a surface that still has it
  ("oswald is no longer used: the lifting line computes the span efficiency"). No shipped aircraft and no README
  entry use it. Waxwing's copy of the format is updated in a Waxwing session.
- `StaticStability.Loads` settles the downwash lag (evaluate, advance by a long step, evaluate) instead of relying on
  `Reset()`, so it is valid for tailed aircraft too.

## 4. Tests

Unit (`tests/SimLab.Flight.Tests/Aero/`):

- `VortexMathTests`: a long segment gives the infinite-line velocity 1/(2πh); a semi-infinite line gives half; the core
  bounds the velocity on the line.
- `LuDecompositionTests`: solves a known system; throws on a singular matrix.
- `LiftingLineTests`: bound-segment orientation (lift along the normal for wing and fin strips); a very high aspect
  ratio wing tends to the 2D result Γ = ½ V c a α; one Newton step converges with a linear polar; symmetry; lag reaches
  63 % after τ; robustness at 40°, in reversed flow and at 0.05 m/s (finite, at most 8 iterations); `Reset`.
- `LiftingLineValidationTests` (through `SurfaceAeroModel`, linear 2π test polar, AVL references from the prototype
  table): rectangular AR 6 (12 strips) CLα within 4 % and Clp within 8 %, 12 vs 24 strips within 2 %; span efficiency
  of the rectangular wing between 0.9 and 1.1; 5° dihedral wing Clβ within 10 %; wing + 16-strip fin CYβ, Cnβ, Clβ
  within 10 %; flying-wing planform CLα within 6 %.
- Existing `SurfaceAeroModelTests` adapted where they encoded the old model: the finite-wing lift test uses Helmbold
  (0.93–1.03), the pitch-rate lift test compares with the lift at α = q (c/2)/V, the downwash test uses `TailDownwash`,
  the swept-elevon flap-moment test resets the model between its two evaluations.

Fleet (`tests/SimLab.Flight.Tests/Behavior/`):

- `StabilityDerivatives`: the investigation's finite-difference harness as a test utility.
- `Golden/avl-derivatives.json`: AVL's derivatives, airspeed and alpha per aircraft (from the investigation's runs of
  the main-branch data); regenerated with `compare_avl.py --write-fixture` when a geometry changes (needs Waxwing and
  AVL locally). An aircraft added later (e.g. `feat/jet-f16`) needs its own entry to join the test.
- `AvlDerivativeTests`: criteria 1–4 per aircraft, a report test with the full ratio table, and a timing test for
  criterion 6.

## 5. Reverting compensating tunings

After §2–§4 are in and measured with the current data, these values go back to their originals (from
`docs/tuning-log.md`), and the AVL fixture is regenerated because the geometry changes:

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

Every behavior test is then run. For the failing ones, the smallest data changes that pass (tail-surface strip counts
included) are gathered into one proposal, **submitted to the user before they are applied**, then logged in
`docs/tuning-log.md`.

## 6. Order of work

1. Numerics (`VortexMath`, `LuDecomposition`), `LiftingLine` and their unit tests; nothing else touched.
2. Integration in `SurfaceAeroModel` (removals, lag, ground effect, `oswald`), validation tests, derivative utility,
   AVL fixture and test; measure criteria 1–4 and 6 and the behavior tests on the current data. **Stop and report to
   the user** before step 3 (and before switching method if the flying wing misses criteria 1–3).
3. Section 5 revert, fixture regeneration, behavior tests, re-tuning proposal → user approval → apply and log.
4. Golden file `frame-invariance.json` regenerated (intended physics change); realism backlog #1, #3, #5, #11, #14
   updated; investigation note with before/after ratios.

## Out of scope

Ground-effect image vortices (the McCormick factor stays), wake roll-up or a wake that follows the flow, unsteady
(Theodorsen / Wagner) lift beyond the downwash lag, fuselage aerodynamics (side force, Munk moment), several chordwise
panels, prop-wash changes, Waxwing changes.
