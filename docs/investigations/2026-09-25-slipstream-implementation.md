# Slipstream implementation: physical prop wash, swirl, and a 3D aircraft (2026-09-25)

Follow-up to `2026-09-25-sport-hover.md` (the sport could not hold a prop hang). Branch `feat/godot-game`, on top of
`d7147da`. Implements the approved design: **A** slipstream swirl, **B** a physical wash footprint, **C** a new
`aircraft/3d`.

## 1. Summary

- The prop wash is now a field (`src/SimLab.Flight/Aero/PropWash.cs`): momentum-theory velocity and contraction behind
  the disk, a mixing jet downstream whose momentum flux stays equal to the thrust, a smooth radial profile, and a swirl
  that carries a calibrated share of the prop torque. Strips are weighted by their actual spanwise overlap with it.
- Two propulsion corrections turned out to be needed for a sane hover: the motor reaction on the airframe no longer
  includes the rotor friction, and the P-factor inflow angle is measured against the total flow through the disk
  (freestream + induced) instead of switching on at 1 m/s with a full 90° angle.
- The sport can just hold a prop hang: full-aileron margin 0.98 → **1.52** (converged in the strip count; 1.59 at the
  shipped 12 strips). With the static hover trim held, the closed-loop pilot keeps it within 3.6° tilt and 2.6° roll
  with no saturation; without the trim it is lost in yaw (realism backlog #12). This needed one data change, approved
  by the user (ailerons from the fuselage side on a 12-strip wing), logged in `docs/tuning-log.md`.
- Control surfaces now cover strips fractionally (review fix), so results converge with the strip count.
- The new **3D 1.2 m** hangs on the prop with a margin of 3.9 and no saturation, and flies hands-off near trim.
- Every other behavior test passed with the new physics without changes; the frame-invariance goldens were
  regenerated (twice: after the slipstream and after the fractional coverage).

Review fixes (after the first version of this report): fractional control coverage (section 2.2), the sport's hover
threshold set from the converged margin (1.45), the sport's closed-loop test scoped to "with the trim held", the 3D
re-trimmed (1.3 kg, CG 0.52, −1.5° stab), `inducedVelocity` required in `PowerPlantLoads.Compute`, and the numbers
below corrected.

## 2. Model

### 2.1 Axial wash (B)

With T the thrust, R the prop radius, A = πR², V₀ ≥ 0 the freestream along the thrust axis and s the distance behind the
disk (body axes, along −thrust axis):

| Quantity | Formula | Source |
|---|---|---|
| Induced velocity at the disk | T = 2ρA v_i (V₀ + v_i) → v_i = −V₀/2 + √(V₀²/4 + T/(2ρA)) | Glauert momentum theory; McCormick, *Aerodynamics, Aeronautics and Flight Mechanics*, §6.2 |
| Ideal axial velocity | w(s) = v_i (1 + s/√(s² + R²)): v_i at the disk, 1.71 v_i at 1 R, 1.95 v_i at 3 R → 2 v_i | uniformly loaded actuator disk / vortex cylinder on the axis (McCormick, *Aerodynamics of V/STOL Flight*, ch. 4; Conway, JFM 297, 1995) |
| Stream-tube radius | R_s = R √((V₀ + v_i)/(V₀ + w(s))) → R/√2 static | continuity |
| Momentum flux carried | M(s)/ρ = (V₀ + v_i) πR² w(s) → T/ρ once the pressure has recovered | momentum theory |
| Potential core length | x_c = 4 · 2R_∞ / λ, λ = 2v_i / (2v_i + 2V₀) | round jets: core ≈ 4–6 d (Rajaratnam, *Turbulent Jets*, 1976, ch. 3; Pope, *Turbulent Flows*, §5.1); prop jets mix sooner (tip vortices, swirl): marine propeller-jet studies report a zone of flow establishment of roughly 2–3.5 prop diameters (reviewed by Lam et al., Ocean Engineering, 2011). 4 contracted diameters = 2.83 prop diameters. Coflow slows the mixing in proportion to the Brown–Roshko velocity ratio (Brown & Roshko, JFM 64, 1974). |
| Centreline velocity | w(s) for s < x_c, w(s)·x_c/s beyond (1/s decay of a round jet) | Rajaratnam; Pope |
| Radial profile | g(r) = 1 for r ≤ a, ½(1 + cos(π(r − a)/L)) for a < r < a + L, 0 beyond | — |
| Core radius | a = 0.85·R_s·(1 − s/x_c) for s < x_c, else 0 (0.85: blade loading falls to zero at the tip, Prandtl tip loss) | — |
| Edge width L | solved at every station from ∫ρ(V₀ + u)u dA = M(s): with I_k = πa² + 2πL(a∫gᵏdt + L∫t gᵏdt), ∫g = ½, ∫g² = 3/8, ∫tg = ¼ − 1/π², ∫tg² = 3/16 − 1/π², a quadratic in L | — |

The wash is applied along the thrust axis in body axes, as before, and nothing is induced ahead of the disk. Everything
is continuous in V₀: in cruise λ is small, the core is long and the wash small (2 N of thrust at 18 m/s adds about
1.2 m/s at the tail).

Sport, static hover (T 21.6 N, v_i 11.0 m/s):

| Station | s (m) | Centre u (m/s) | Tube R_s | Core a | Half-velocity r | Outer r | u at r = 0.1 m | Swirl at r = 0.1 m |
|---|---|---|---|---|---|---|---|---|
| Disk | 0 | 11.0 | 0.152 | 0.130 | 0.160 | 0.190 | 11.0 | 0.60 m/s (3.1°) |
| 1 R | 0.15 | 18.8 | 0.117 | 0.082 | 0.127 | 0.172 | 16.9 | 0.88 m/s (3.0°) |
| Wing root quarter chord | 0.44 | 21.4 | 0.109 | 0.046 | 0.125 | 0.205 | 15.8 | 0.69 m/s (2.5°) |
| Stab | 1.17 | 16.1 | 0.108 | 0 | 0.177 | 0.353 | 13.1 | 0.23 m/s (1.0°) |

Old model for comparison: 17.6 m/s everywhere behind the disk inside r = 0.152, momentum flux 1.28 T.

### 2.2 Overlap weighting (B)

Each strip samples the wash at 8 points spread evenly along its quarter-chord line (`SurfaceSegment.HalfSpan`, new). The
mean relative velocity sets the angle of attack; the mean of the squared in-plane speed sets the dynamic pressure (so a
strip half in the jet gets half its q, not the q at its centre). The station is taken at the strip's centre. The
samples depend only on the wash and the geometry, so they are cached while the wash is unchanged (the four RK4
evaluations of a step): results are bit-identical and the flight test run went from 47 s to 33 s.

Control surfaces cover strips fractionally: a strip partly covered by a control gets that fraction of the flap's
lift, moment and drag increments (before, a strip was all control or none, by its centre). Results converge with the
strip count (wing strips 12/24/48/96, tails n/4):

| Aircraft | Cruise full-aileron roll (°/s, pb/2V) | Hover margin | Airframe recovers |
|---|---|---|---|
| Sport | 391, 0.227 at every count | 1.59 / 1.53 / 1.52 / 1.52 | 40.0 / 39.4 / 39.3 / 39.3% |
| 3D | 385–384, 0.288–0.287 | 3.87 / 3.86 / 3.82 / 3.82 | 54.5 / 53.6 / 53.3 / 53.3% |
| Trainer | 165–164, 0.144–0.143 | — | — |
| Wing | 204, 0.140 | — | — |

With centre assignment the sport's margin was 1.57 at 12 strips, 1.32 at 20 and 1.49 at 48–96 (reviewer): the first
version of the hover test passed by discretisation.

### 2.3 Swirl (A)

The torque the prop gives the air leaves as angular momentum flux Q = ∫ρ(V₀ + u) r v_θ dA (Glauert's general momentum
theory). The swirl is v_θ(r) = Ω(s)·r·g(r) (solid-body near the hub, peaking in the outer jet, zero outside it, the
shape of measured swirl-angle distributions, Veldhuis, *Propeller Wing Aerodynamic Interference*, TU Delft 2005, ch. 3).
Ω(s) is solved at every station so that the flux equals η·Q, with Q the prop's aerodynamic torque
(`PowerTelemetry.PropTorque`, new) and η = `PropWash.SwirlEfficiency`. It turns with the prop: the air's angular
velocity is spinDirection·Ω along the thrust axis, and a strip sees minus that. As the jet spreads, Ω falls.

### 2.4 Propulsion corrections found on the way

- **Reaction torque.** The motor puts ke·I on the rotor, but the bearing and iron-loss friction acts between rotor and
  stator, so the airframe only receives ke·I − friction, which in steady state is the prop torque
  (`PowerTelemetry.ReactionTorque`, new; realism backlog #1 had flagged it). Sport hover: 0.44 → 0.42 N·m.
- **P-factor.** The offset grew with crossflow/|inflow| and was switched off below 1 m/s. In a hover, any drift above
  1 m/s gave the full 90° offset (0.1 D = 3 cm, 0.65 N·m of yaw on the sport), with a jump at 1 m/s; in the closed-loop
  hover this saturated the rudder. The inflow angle is now measured against the total axial flow through the disk,
  |V_axial| + v_i: 1 m/s of drift under a hovering prop is a 5° inflow angle. In cruise v_i is small and the change is
  a few percent. (`PowerPlantLoads.Compute`, test `P_factor_in_a_hover_is_small_and_grows_smoothly_with_drift`.)

## 3. Calibration of the swirl constant

With η = 1 the strip model recovers **2.4 Q** in the sport's static hover (3.2 Q in the spike's field): physically
impossible, since the airframe cannot take more angular momentum out of the jet than the prop put in. The reasons are
known limits of strip theory here: each surface in the jet sees the whole swirl (the wing root does not deplete it for
the stab and fin), and 5 cm strips get 2D lift slopes. η is therefore a calibration, set so that the sport's airframe
(wing root, stab, fin) recovers **40% of the prop torque** in a static hover at hover throttle, controls neutral
(`HoverTests.Sport_airframe_recovers_about_forty_percent_of_the_prop_torque_in_a_hover`, band 35–45%):
**η = 0.165**. The 30–60% plausible range comes from the stator analogy: a purpose-built stator recovers most of the
swirl, a wing root, stab and fin are a partial, badly placed one.

The recovered share is linear in η:

| η | Sport recovers | Sport roll bias (N·m) | Sport full-aileron margin | Sport closed-loop hover | 3D recovers | 3D margin | Trainer recovers |
|---|---|---|---|---|---|---|---|
| 0.124 | 30.1% | 0.328 | 1.38 | holds (3.6° tilt, 2.9° roll) | 40.6% | 3.09 | 15.6% |
| **0.165** | **40.0%** | **0.287** | **1.57** | **holds (3.6°, 2.6°)** | **53.9%** | **3.92** | **20.7%** |
| 0.248 | 59.8% | 0.204 | 2.16 | holds (3.6°, 2.1°) | 80.7% | 8.91 | 31.1% |

(Measured before the fractional coverage, which moves the sport's margin by −0.05 at convergence.) At the low end of
the range the sport's margin falls to about 1.35 at convergence; it still hangs in the closed loop with the trim held. The 3D recovers more because its surfaces sit closer to the axis (mid wing, ailerons to the root, tall
fin). A side effect of calibrating the field rather than the extraction: the swirl angle a surface sees is about 1/6 of
the full physical swirl (2.5° instead of ~15° at the sport's wing root at r = 0.1 m). A better model would keep the
full swirl and deplete it along the airframe (a stator-like extraction, or a lifting-line interaction); VSPAERO could
calibrate it.

## 4. Before / after

"Before" is `d7147da`; the hover rows use the final pilot (`Hover.Fly`) on both trees. Moments in body axes (+x roll
left, +y pitch up, +z yaw left), N·m.

| Quantity | Before | After |
|---|---|---|
| Sport hover throttle | 0.773 | 0.773 |
| Sport hover bias (roll, pitch, yaw) | (0.504, 0.462, −0.691) | (0.287, 0.350, −0.435) |
| Sport full aileron in hover | 0.495 | 0.455 at 12 strips, 0.440 converged (0.193 with the old aileron data) |
| Sport roll margin | 0.98 | **1.59** at 12 strips, **1.52** converged (0.67 with the old aileron data) |
| Sport full elevator / rudder in hover | 4.13 / 1.98 | 2.93 / 1.05 |
| Sport closed-loop prop hang, 10 s, trim held | roll control lost: max roll 82.5°, aileron saturated 87% | max tilt 3.6°, max roll 2.6°, no saturation |
| Same without the trim | — | rudder saturated 42%, lost at 5.7 s |
| Sport new aileron data on the old physics | margin 1.36, holds (5.2° roll) | — |
| 3D (first 1.5 kg version) on the old physics / new | margin 2.34, holds | margin 3.92, holds |
| Trainer pitch moment change, cruise → full throttle, frozen at 15 m/s | −0.024 (0% of full up elevator) | +0.084 (1.4% of 6.0) |
| Trainer idle → full throttle at 10 m/s, α 5° | −0.061 | +0.124 |
| Trainer pitch attitude 3 s after full throttle (cruise throttle) | 9.0° (−5.8°) | 11.3° (−5.1°) |
| Sport idle → full throttle at 10 m/s, α 5° | 0.573 | 0.674 |
| Hands-off cruise bank, sport, 10 s / 30 s | −25.4° / −28.0° | −23.2° / −26.4° |
| Hands-off cruise bank, trainer, 10 s / 30 s | −15.8° / −21.7° | −13.6° / −19.9° |
| Wing, full throttle hands-off from 14 m/s, bank after 5 s | −83.0° | −82.8° |
| Sport full-aileron roll at 18 m/s | 421°/s, pb/2V 0.245 | 391°/s, 0.227 |
| Trainer / wing full-aileron roll | 205 / 228°/s (pb/2V 0.179 / 0.156) | 166 / 206°/s (0.145 / 0.141): fractional coverage, the controls no longer run to the strip ends |

Realism backlog:
- **#2 (trainer power pitch-up)**: the wash hypothesis does not hold. The wash moment was ~0 before and is 1.4% of the
  elevator now (the new wash is stronger at the tail in cruise because it stays coherent in coflow instead of the old
  0.8 factor); the 2° down thrust cancels most of it. The pitch-up is the climb response of a speed-stable aircraft
  with T/W ≈ 1. The attitude change is slightly larger than before (16.4° vs 14.8° relative to cruise throttle), so #2
  did not "improve"; it is re-classified as probably realistic, to check against a real full-throttle step.
- **#1**: small improvement (swirl and the friction fix). Whether swirl explains the rest is not testable with the current calibration (the field is scaled to about 1/6 of the physical swirl angle).
- **#10**: unchanged; the wing's pusher prop has no surface in its swirl.

Frame-invariance goldens (regenerated after the slipstream, `86b7bdb`, and after the fractional coverage), final-state
position difference against the pre-slipstream goldens (`d7147da`):

| Scenario | Final position diff | Notes |
|---|---|---|
| trainer_wind (6 s) | 9.3 m | heading 100.9° → 110.7°, roll −28.0° → −26.4° |
| wing_launch (5 s) | 5.5 m | bank at 5 s −63.4° → −71.8°, altitude 9.1 → 12.3 m |
| sport_rolls (5 s) | 12.2 m | the aileron data and coverage (roll rate) plus the wash |
| trainer_takeoff (8 s) | 21.9 m | the scripted climb with 0.1 rudder ends in an uncontrolled roll in both versions |

No crash state changed.

## 5. Test and data changes

New tests:
- `Aero/PropWashTests` (16 cases): disk velocity = momentum-theory v_i; nothing ahead of the disk or without thrust;
  acceleration to 1.7 v_i at 1 R and 1.9–2 v_i at 3 R, tube radius R/√2; momentum flux = T within −4%/+1% from 3 R to
  20 R, static and at 10 m/s; smooth, decreasing radial profile (no step > 3% of the centre velocity per 0.5 mm);
  decay and spreading downstream; continuity in airspeed and a small wash at high advance ratio; swirl sense for both
  spin directions; angular momentum flux = η·Q at several stations and speeds.
- `Aero/SurfaceAeroModelTests`: a centred wash on a symmetric wing gives no roll or yaw; a 2 mm lateral shift of the
  prop changes lift by < 1% and roll smoothly, and the roll moment is antisymmetric (overlap weighting); swirl opposes
  the motor reaction for both spin directions and recovers less than the torque.
- `Propulsion/PropulsionTests`: the airframe reaction equals the prop torque once spun up; P-factor in a hover is
  5–12% of its 90° value at 1 m/s drift and has no jump at 1 m/s.
- `Behavior/Hover.cs` (helper next to `Fleet`/`StaticStability`): hover throttle, total moment in a frozen state
  (M = I·Δω/Δt after the servos, rotor and downwash settle), static stick authorities, and the closed-loop pilot: PD
  attitude (ζ 0.8, ωn 6 rad/s), zero latency, stick limits, the static hover trim as a stick offset, a lean of at most
  8° against drift, and an altitude hold. The trim is needed: a pure PD holds the pitch and yaw biases with a steady
  tilt, the aircraft drifts, and the fin in the slipstream weathercocks the nose into the drift (a real tail-sitter
  effect) until the rudder saturates.
- `Behavior/HoverTests`: sport full-aileron margin ≥ 1.45 (the converged 1.52 less the spread of the swirl-calibration
  band); sport 10 s prop hang with the trim held, within ±15° and with no saturation on any channel (the no-trim run
  is printed); swirl calibration (35–45%); 3D margin ≥ 1.5, prop hang within ±15° and no aileron saturation; a report
  of the numbers in section 4 (sport, trainer, 3d).
- `Aero/SurfaceAeroModelTests`: the aileron roll moment is within 3% of the 96-strip value from 6 strips up and moves
  about 1% for a 1% move of its inner end; adjacent controls may share a strip, overlapping ones are rejected.

Changed tests:
- `SurfaceAeroModelTests.Prop_wash_blows_over_a_stationary_tail`: same assertion, built with the new `PropWash.Create`
  (the old constructor took a velocity and a radius).
- `PropulsionTests.Loads_contain_thrust_and_a_roll_left_reaction_for_a_clockwise_prop`: the moment now comes from
  `ReactionTorque` (the test sets it; same assertion).
- `RollRateTests` skip reason updated with the current pb/2V (0.227); the test stays skipped (0.10–0.20 band).
- `SurfaceAeroModelTests.Control_that_covers_no_segment_is_rejected`: now a zero-span control (a 0.5%-span control is
  represented as a fraction of a strip).
- `PropulsionTests`: `inducedVelocity` passed explicitly (no longer optional).
- The frame-invariance goldens (section 4).
- "3d" added wherever the fleet is enumerated: `FleetDefinitionTests` (mass 1.3, wing area 0.336, T/W > 1.8, wingtip
  hull height), `FleetControlSignTests` (all four), `ControlResponseTests` (all three), `RollRateTests.Report`,
  `GroundHandlingTests.Rests_on_its_gear_without_drifting`, `FlightQualityTests.Flies_hands_off_for_ten_seconds` and
  `Dutch_roll_damps_after_a_rudder_pulse`, `FlightSessionTests` (catalog, ground check), `SoundSpecTests`,
  `StaticRunUpTests`, `ReactiveAttitudeTests`; `Fleet.Cruise("3d")` = 14 m/s, throttle 0.45.

The only assertion changed is the sport hover margin threshold (1.5 → 1.45, set from the converged value at the
reviewer's request, see above).

Data change (`docs/tuning-log.md`, 2026-09-25): sport wing `segments` 6 → 12 and aileron `spanStart` 0.15 → 0.08.
With the contracted slipstream (≈ 0.11 m radius at the wing), an aileron starting at 9 cm barely reaches it; low-wing
aerobatic models run strip ailerons from the fuselage side (≈ 5 cm here). Approved by the user. The first version
also needed 12 strips because controls were assigned by strip centre; with fractional coverage the strip count no
longer matters (the 12 strips are kept). The cruise roll drop first reported (421 → 368°/s) was mostly that
discretisation (the aileron ended at 0.917 of the panel instead of 0.95); converged it is 391°/s (backlog #11).

3D re-trim (review, logged): mass 1.5 → 1.3 kg, CG 0.50 → 0.52, stab incidence 0 → −1.5° (section 6).

## 6. The 3D 1.2 m (`aircraft/3d`)

Derived from the sport's stations (nose datum, same wing planform), with 3D proportions:

| Item | Value | Why |
|---|---|---|
| Mass | 1.3 kg | a 1.2 m profile/foam 3D model on a 4S 2200 pack weighs about 1.0–1.3 kg (pack 0.24, motor 0.13, ESC and servos 0.13, airframe 0.5–0.7); wing loading 3.9 kg/m² (sport 6.5) |
| Inertia (roll, pitch, yaw) | 0.045, 0.09, 0.125 kg·m² | sport scaled by mass, foam wing (≈ 0.25 kg·1.2²/12 + fuselage and servos) |
| Power | sport's 4S 750 kV motor, 12x6, 2.2 Ah pack | static thrust 31.8 N, **T/W 2.5** (3D models on 4S commonly 2–3), 50 A at full throttle |
| Hover throttle | 0.57 | the 50–70% quoted for T/W ≥ 2 3D models |
| Wing | 1.2 m, chords 0.32/0.24 (0.336 m²), mid wing, 0° dihedral, 0° incidence, NACA 0012, 20 strips | symmetrical section, neutral 3D setup |
| Ailerons | 5–100% of the half span, 35% chord, ±40° | 3D strip ailerons |
| Stab / elevator | 0.52 m span, chords 0.24/0.20, −1.5° incidence; elevator 50% chord, ±45° | larger than the sport's (0.114 vs 0.075 m²); the incidence trims it at 14 m/s |
| Fin / rudder | 0.26 m, chords 0.28/0.20, 10° sweep; rudder 55% chord, ±45° | large 3D rudder |
| Thrust line | [−1, 0, 0] | 0° right thrust |
| CG | 0.52 m (37% MAC) | usual 3D balance (30–40% MAC) |
| Static margin | 18% MAC (downwash settled, power off) | 10–20% for a 3D model; the first version (CG 0.50, 0° stab) had 25% and trimmed at zero lift near 35 m/s |
| Trim at 14 m/s | alpha 4.2°, elevator −0.003 | stick at centre |
| Provenance | estimated | |

Results: static hover bias (0.115, 0.313, 0.092) N·m, full aileron 0.449, elevator 3.03, rudder 1.47 N·m, **roll
margin 3.9**, airframe recovers 54% of the prop torque; closed-loop prop hang: max tilt 3.3°, max roll 1.8°, no
saturation. At 14 m/s: full aileron 385°/s (pb/2V 0.29, a 3D model at high rates), full elevator 186°/s. Hands-off at
throttle 0.45 from 14 m/s, stick centred: it stays near 13–15 m/s and slowly banks left with the motor torque (−29.8°
after 10 s, −31.7° after 30 s, still flying), like the sport (−26.4°). Full throttle pitches it up strongly (+0.70 N·m,
10% of full up elevator: the −1.5° stab sits in the jet core). The first report's "hands-off bank −2.2°" came from
the first version, which had already hit the ground.

Godot, headless: `--smoke-flight 3d 5` → `SIMLAB_SMOKE_OK aircraft=3d t=5.00 crash=None`; `--render-audio 3d` →
`SIMLAB_AUDIO_OK samples=882000`. (Both runs also print missing `.godot/imported` impact samples, the same for the
sport: the worktree has no import cache.)

## 7. Concerns and limits

- **The sport's margin is small**: 1.52 converged, about 1.35 at the low end of the swirl range. It is a sport
  aerobat that can just hold a prop hang, and only with the hover trim held (backlog #12).
- **Swirl is calibrated in the field**, not in the extraction (section 3), and only for a static hover. The swirl
  angles seen by the surfaces are small (1–3°); swirl effects in climb and cruise (fin yaw, the reason for right
  thrust) are probably under-represented.
- **The wash follows the thrust axis**; it is not bent by crossflow or sideslip, and there is no hub/spinner deficit.
  Nothing is induced ahead of the disk (pusher props, canards).
- **The station is taken at the strip centre**; with 8 samples along the span the chordwise variation is ignored.
- **The induced velocity ignores the crossflow** (momentum theory on the axial inflow only; backlog #13).
- The closed-loop pilot is zero-latency and knows the static trim; it proves authority and margins, not human
  flyability.
- The sport's elevator and rudder in hover dropped (4.1 → 2.9 and 2.0 → 1.05 N·m) because the stab and fin now sit in
  a jet that has started to decay and spread; still ample in the closed loop.
